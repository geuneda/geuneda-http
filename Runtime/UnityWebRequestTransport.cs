using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using UnityEngine.Networking;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// <see cref="UnityWebRequest"/> 로 요청을 보내는 기본 전송입니다. 어느 스레드에서 불러도 메인 스레드로 옮겨 보냅니다.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 4xx, 5xx 는 상태 코드와 본문이 담긴 응답(성공 <c>Result</c>)입니다. 연결 실패는 <c>NetworkError</c>, 내려받기 처리 오류는
	/// <see cref="HttpResultCodes.InvalidResponse"/>, <see cref="HttpRequest.Timeout"/> 초과는 <c>Timeout</c>, 호출자 취소는 <c>Cancelled</c> 입니다.
	/// 타임아웃과 취소 때는 요청을 중단합니다.
	/// </para>
	/// <para>
	/// UnityWebRequest 예외를 쓰지 않고 완료를 기다린 뒤 <see cref="UnityWebRequest.result"/> 로 나눕니다. 잘못된 헤더 같은 오용 예외는 그대로 던집니다.
	/// </para>
	/// <para>
	/// 본문이 있으면 Content-Type 은 <see cref="HttpRequest.ContentType"/> 을 쓰고, 헤더 사전의 Content-Type 은 무시합니다.
	/// 메인 스레드로 옮기는 동안 취소되면 요청을 보내지 않고 <c>Cancelled</c> 입니다.
	/// </para>
	/// </remarks>
	public sealed class UnityWebRequestTransport : IHttpTransport
	{
		private const string ContentTypeHeader = "Content-Type";

		public async UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, CancellationToken cancellationToken)
		{
			if (request == null)
			{
				throw new ArgumentNullException(nameof(request));
			}

			if (!request.Uri.IsAbsoluteUri)
			{
				throw new ArgumentException($"Transport needs an absolute uri: {request.Uri}", nameof(request));
			}

			if (cancellationToken.IsCancellationRequested)
			{
				return Result<HttpResponse>.Fail(ResultCodes.Cancelled);
			}

			await UniTask.SwitchToMainThread();

			// 메인 스레드로 옮기는 동안 취소됐으면 보내지 않는다(POST 처럼 서버 상태를 바꾸는 요청을 시작하지 않게)
			if (cancellationToken.IsCancellationRequested)
			{
				return Result<HttpResponse>.Fail(ResultCodes.Cancelled);
			}

			using (var timeout = new CancellationTokenSource())
			using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
			using (var webRequest = CreateWebRequest(request))
			{
				if (request.Timeout.HasValue)
				{
					timeout.CancelAfter(request.Timeout.Value);
				}

				var operation = webRequest.SendWebRequest();

				try
				{
					await UniTask.WaitUntil(() => operation.isDone, PlayerLoopTiming.Update, linked.Token);
				}
				catch (OperationCanceledException)
				{
					webRequest.Abort();

					return cancellationToken.IsCancellationRequested
						? Result<HttpResponse>.Fail(ResultCodes.Cancelled)
						: Result<HttpResponse>.Fail(ResultCodes.Timeout, $"{request} timed out after {request.Timeout}");
				}

				return ToResult(request, webRequest);
			}
		}

		private static UnityWebRequest CreateWebRequest(HttpRequest request)
		{
			var webRequest = new UnityWebRequest(request.Uri, request.Method.ToVerb())
			{
				downloadHandler = new DownloadHandlerBuffer()
			};

			try
			{
				if (request.Body != null)
				{
					webRequest.uploadHandler = new UploadHandlerRaw(request.Body) { contentType = request.ContentType };
				}

				foreach (var header in request.Headers)
				{
					// 본문의 Content-Type 은 HttpRequest.ContentType 이 기준이다. 헤더 사전의 값으로 덮어쓰지 않는다
					if (request.Body != null && string.Equals(header.Key, ContentTypeHeader, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					webRequest.SetRequestHeader(header.Key, header.Value);
				}

				return webRequest;
			}
			catch
			{
				webRequest.Dispose();
				throw;
			}
		}

		private static Result<HttpResponse> ToResult(HttpRequest request, UnityWebRequest webRequest)
		{
			switch (webRequest.result)
			{
				case UnityWebRequest.Result.Success:
				case UnityWebRequest.Result.ProtocolError:
					return Result<HttpResponse>.Ok(new HttpResponse(request.Uri, (int)webRequest.responseCode,
						webRequest.downloadHandler.data, webRequest.GetResponseHeaders() ?? new Dictionary<string, string>()));
				case UnityWebRequest.Result.DataProcessingError:
					return Result<HttpResponse>.Fail(HttpResultCodes.InvalidResponse, $"{request}: {webRequest.error}");
				default:
					return Result<HttpResponse>.Fail(ResultCodes.NetworkError, $"{request}: {webRequest.error}");
			}
		}
	}
}
