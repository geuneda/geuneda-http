using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// 모든 요청에 공통 헤더(앱 버전, 플랫폼 등)를 붙입니다. 요청에 이미 있는 헤더는 바꾸지 않습니다.
	/// 본문의 Content-Type(<see cref="HttpRequest.ContentType"/>)이 있으면 Content-Type 기본값은 붙이지 않습니다.
	/// </summary>
	/// <remarks>
	/// 값은 만들 때 정해집니다. 요청마다 바뀌는 값(세션 ID, 토큰)은 직접 미들웨어를 만들어 <c>WithHeader</c> 로 붙입니다.
	/// </remarks>
	public sealed class DefaultHeadersMiddleware : IHttpMiddleware
	{
		private readonly KeyValuePair<string, string>[] _headers;

		/// <exception cref="ArgumentNullException"><paramref name="headers"/> 가 null 인 경우</exception>
		public DefaultHeadersMiddleware(IReadOnlyDictionary<string, string> headers)
		{
			if (headers == null)
			{
				throw new ArgumentNullException(nameof(headers));
			}

			_headers = new KeyValuePair<string, string>[headers.Count];

			var index = 0;
			foreach (var header in headers)
			{
				_headers[index++] = header;
			}
		}

		public UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, HttpHandler next, CancellationToken cancellationToken)
		{
			var withDefaults = request;

			foreach (var header in _headers)
			{
				if (!HasHeader(withDefaults, header.Key))
				{
					withDefaults = withDefaults.WithHeader(header.Key, header.Value);
				}
			}

			return next(withDefaults, cancellationToken);
		}

		// 본문의 Content-Type 은 헤더가 아니라 HttpRequest.ContentType 에 있다
		private static bool HasHeader(HttpRequest request, string name)
		{
			return request.Headers.ContainsKey(name) ||
				(request.ContentType != null && string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase));
		}
	}
}
