using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// 버퍼 요청을 <see cref="HttpRequest"/> 로 바꿔 <see cref="IHttpTransport"/>(기본 <see cref="UnityWebRequestTransport"/>)로 보냅니다.
	/// 네이티브 전송이 없는 에디터·iOS 용이고 요청마다 할당합니다.
	/// </summary>
	public sealed class UnityWebRequestBufferTransport : IHttpBufferTransport
	{
		private readonly IHttpTransport _transport;

		public UnityWebRequestBufferTransport() : this(new UnityWebRequestTransport())
		{
		}

		public UnityWebRequestBufferTransport(IHttpTransport transport)
		{
			_transport = transport ?? throw new ArgumentNullException(nameof(transport));
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

			response.Reset();

			if (!HttpBufferFrame.IsAbsoluteHttpUrl(request.Url))
			{
				throw new ArgumentException($"Transport needs an absolute http(s) url: {request.Url}", nameof(request));
			}

			// 주소 형식 오류는 HttpRequest 가 ArgumentException 으로 던진다
			var converted = new HttpRequest(request.Method, request.Url);

			for (var i = 0; i < request.HeaderCount; i++)
			{
				converted = converted.WithHeader(request.GetHeaderName(i), request.GetHeaderValue(i));
			}

			if (request.HasBody)
			{
				converted = converted.WithBody(request.Body.ToArray(), request.ContentType);
			}

			if (request.Timeout.HasValue)
			{
				converted = converted.WithTimeout(request.Timeout.Value);
			}

			var result = await _transport.SendAsync(converted, cancellationToken);

			if (!result.TryGetValue(out var received))
			{
				return Result.Fail(result.Code, result.Message);
			}

			var body = received.Body ?? Array.Empty<byte>();
			body.CopyTo(response.Begin(received.StatusCode, body.Length), 0);
			return Result.Ok();
		}

		/// <summary>가진 자원이 없습니다. 넘겨받은 전송은 닫지 않습니다.</summary>
		public void Dispose()
		{
		}
	}
}
