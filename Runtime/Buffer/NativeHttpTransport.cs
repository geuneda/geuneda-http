using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// 네이티브 HTTP(안드로이드는 Java 스레드의 <c>HttpURLConnection</c>)로 보내는 버퍼 전송입니다. 데워진 뒤에는 요청마다 C# 할당이 없습니다.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 어느 스레드에서 불러도 메인 스레드로 옮겨 보냅니다. 요청을 프레임(<see cref="HttpBufferFrame"/>)으로 써서 넘기고, 매 프레임 상태 숫자를 읽어
	/// 끝나면 본문을 응답 버퍼에 복사합니다. 제한 시간은 시작부터 끝까지 전체 시간이고(네이티브 연결·읽기 제한도 같은 값), 넘거나 호출자가 취소하면
	/// 네이티브 요청을 끊습니다. 메인 스레드로 옮기는 동안 취소되면 보내지 않습니다.
	/// </para>
	/// <para>
	/// 잘못된 주소(포트 등)·헤더 값은 네이티브가 요청을 만들 때 걸러 <see cref="ArgumentException"/> 입니다.
	/// </para>
	/// <para>
	/// 동시에 보내는 요청 수만큼 통로를 만들어 두고 다시 씁니다. <see cref="HttpMethod.Patch"/> 는 <c>HttpURLConnection</c> 이 받지 않아
	/// <see cref="NotSupportedException"/> 입니다. 응답 헤더는 읽지 않습니다.
	/// </para>
	/// </remarks>
	public sealed class NativeHttpTransport : IHttpBufferTransport
	{
		private readonly Func<INativeHttpChannel> _createChannel;
		private readonly Stack<INativeHttpChannel> _idle = new Stack<INativeHttpChannel>();
		private byte[] _frame = new byte[16 * 1024];
		private bool _disposed;

#if UNITY_ANDROID
		/// <summary>안드로이드 플레이어에서만 씁니다. Java 플러그인이 없으면 여기서 예외입니다.</summary>
		public NativeHttpTransport() : this(() => new AndroidJniHttpChannel())
		{
		}
#endif

		internal NativeHttpTransport(Func<INativeHttpChannel> createChannel)
		{
			_createChannel = createChannel ?? throw new ArgumentNullException(nameof(createChannel));
			// 첫 통로를 바로 만들어 플러그인이 없으면 만들 때 알린다
			_idle.Push(_createChannel());
		}

		public async UniTask<Result> SendAsync(HttpBufferRequest request, HttpBufferResponse response, CancellationToken cancellationToken)
		{
			if (request == null)
			{
				throw new ArgumentNullException(nameof(request));
			}

			if (response == null)
			{
				throw new ArgumentNullException(nameof(response));
			}

			if (request.Method == HttpMethod.Patch)
			{
				throw new NotSupportedException("HttpURLConnection does not support PATCH");
			}

			// HttpURLConnection 은 본문이 있는 GET 을 POST 로 바꾼다
			if (request.HasBody && (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head))
			{
				throw new NotSupportedException($"{request.Method} with a body is not supported");
			}

			response.Reset();

			if (cancellationToken.IsCancellationRequested)
			{
				return Result.Fail(ResultCodes.Cancelled);
			}

			await UniTask.SwitchToMainThread();

			if (cancellationToken.IsCancellationRequested)
			{
				return Result.Fail(ResultCodes.Cancelled);
			}

			if (_disposed)
			{
				throw new ObjectDisposedException(nameof(NativeHttpTransport));
			}

			var length = HttpBufferFrame.Write(request, ref _frame);
			var channel = _idle.Count > 0 ? _idle.Pop() : _createChannel();

			try
			{
				var deadline = Deadline(request.Timeout);
				channel.Start(_frame, length);
				int state;

				// 취소·해제·제한 시간을 상태보다 먼저 본다(기다리는 사이에 끝났어도 그쪽이 이긴다)
				while (true)
				{
					if (cancellationToken.IsCancellationRequested || _disposed)
					{
						channel.Cancel();
						return Result.Fail(ResultCodes.Cancelled);
					}

					if (Stopwatch.GetTimestamp() >= deadline)
					{
						channel.Cancel();
						return Result.Fail(ResultCodes.Timeout, $"{request} timed out after {request.Timeout}");
					}

					state = channel.Poll();

					if (state != NativeHttpState.Running)
					{
						break;
					}

					await UniTask.Yield();
				}

				switch (state)
				{
					case NativeHttpState.Done:
						var bodyLength = channel.ResponseLength;
						channel.CopyResponse(response.Begin(channel.StatusCode, bodyLength), bodyLength);
						return Result.Ok();
					case NativeHttpState.Timeout:
						return Result.Fail(ResultCodes.Timeout, $"{request} timed out after {request.Timeout}");
					case NativeHttpState.Cancelled:
						return Result.Fail(ResultCodes.Cancelled);
					case NativeHttpState.InvalidRequest:
						// 잘못된 주소·헤더·메서드는 오용이다(UnityWebRequest 쪽과 같게 예외, 통로는 버린다)
						throw new ArgumentException($"Invalid request {request}: {channel.Error}", nameof(request));
					default:
						return Result.Fail(ResultCodes.NetworkError, $"{request}: {channel.Error}");
				}
			}
			catch
			{
				// 상태를 알 수 없는 통로는 다시 쓰지 않는다
				channel.Dispose();
				channel = null;
				throw;
			}
			finally
			{
				if (channel != null)
				{
					if (_disposed)
					{
						channel.Dispose();
					}
					else
					{
						_idle.Push(channel);
					}
				}
			}
		}

		private static long Deadline(TimeSpan? timeout)
		{
			var now = Stopwatch.GetTimestamp();

			if (!timeout.HasValue)
			{
				return long.MaxValue;
			}

			var ticks = timeout.Value.TotalSeconds * Stopwatch.Frequency;
			return ticks >= long.MaxValue - now ? long.MaxValue : now + (long)ticks;
		}

		/// <summary>쉬는 통로를 닫습니다. 보내는 중인 요청은 다음 상태 확인 때 끊고 <c>Cancelled</c> 로 끝납니다.</summary>
		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;

			while (_idle.Count > 0)
			{
				_idle.Pop().Dispose();
			}
		}
	}
}
