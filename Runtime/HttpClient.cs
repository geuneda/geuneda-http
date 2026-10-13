using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Newtonsoft.Json;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// 미들웨어 체인을 거쳐 <see cref="IHttpTransport"/> 로 요청을 보내는 클라이언트입니다. <see cref="HttpClientBuilder"/> 로 만듭니다.
	/// </summary>
	/// <remarks>
	/// 미들웨어는 order 가 작은 것이 바깥(먼저 실행), 같으면 등록한 순서입니다. 체인은 만들 때 한 번 조립하고 바뀌지 않습니다.
	/// 여러 스레드에서 동시에 불러도 됩니다(전송은 메인 스레드로 옮겨 보냄).
	/// </remarks>
	public sealed class HttpClient : IHttpClient
	{
		private readonly HttpHandler _pipeline;
		private readonly JsonSerializerSettings _jsonSettings;

		/// <summary>상대 주소의 기준입니다. 없으면 null 이고, 그때는 절대 주소만 보낼 수 있습니다.</summary>
		public Uri BaseAddress { get; }

		internal HttpClient(Uri baseAddress, IReadOnlyList<IHttpMiddleware> middlewares, IHttpTransport transport, JsonSerializerSettings jsonSettings)
		{
			BaseAddress = baseAddress;
			_jsonSettings = jsonSettings;
			_pipeline = BuildPipeline(middlewares, transport);
		}

		/// <exception cref="ArgumentNullException"><paramref name="request"/> 가 null 인 경우</exception>
		/// <exception cref="ArgumentException">상대 주소인데 <see cref="BaseAddress"/> 가 없는 경우</exception>
		public async UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null)
			{
				throw new ArgumentNullException(nameof(request));
			}

			var resolved = Resolve(request);

			try
			{
				return await _pipeline(resolved, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				// 미들웨어가 취소를 예외로 던져도 결과 규약대로 코드로 바꾼다
				return Result<HttpResponse>.Fail(ResultCodes.Cancelled);
			}
		}

		public async UniTask<Result<T>> SendJsonAsync<T>(HttpRequest request, CancellationToken cancellationToken = default)
		{
			var result = await SendAsync(request, cancellationToken);

			if (!result.TryGetValue(out var response))
			{
				return Result<T>.Fail(result.Code, result.Message);
			}

			if (!response.IsSuccessStatusCode)
			{
				return Result<T>.Fail(HttpResultCodes.HttpError, $"HTTP {response.StatusCode} from {request}");
			}

			return response.ReadJson<T>(_jsonSettings);
		}

		public UniTask<Result<HttpResponse>> GetAsync(string uri, CancellationToken cancellationToken = default)
		{
			return SendAsync(HttpRequest.Get(uri), cancellationToken);
		}

		public UniTask<Result<T>> GetJsonAsync<T>(string uri, CancellationToken cancellationToken = default)
		{
			return SendJsonAsync<T>(HttpRequest.Get(uri), cancellationToken);
		}

		public UniTask<Result<HttpResponse>> PostJsonAsync(string uri, object body, CancellationToken cancellationToken = default)
		{
			return SendAsync(HttpRequest.Post(uri).WithJsonBody(body, _jsonSettings), cancellationToken);
		}

		public UniTask<Result<T>> PostJsonAsync<T>(string uri, object body, CancellationToken cancellationToken = default)
		{
			return SendJsonAsync<T>(HttpRequest.Post(uri).WithJsonBody(body, _jsonSettings), cancellationToken);
		}

		private HttpRequest Resolve(HttpRequest request)
		{
			if (request.Uri.IsAbsoluteUri)
			{
				return request;
			}

			if (BaseAddress == null)
			{
				throw new ArgumentException($"Relative uri '{request.Uri}' needs a base address", nameof(request));
			}

			return request.WithUri(new Uri(BaseAddress, request.Uri));
		}

		private static HttpHandler BuildPipeline(IReadOnlyList<IHttpMiddleware> middlewares, IHttpTransport transport)
		{
			HttpHandler handler = transport.SendAsync;

			for (var i = middlewares.Count - 1; i >= 0; i--)
			{
				var middleware = middlewares[i];
				var next = handler;
				handler = (request, cancellationToken) => middleware.SendAsync(request, next, cancellationToken);
			}

			return handler;
		}
	}
}
