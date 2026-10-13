using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// <see cref="HttpClient"/> 를 만듭니다. 기본 주소, 미들웨어(order), 전송, JSON 설정을 정한 뒤 <see cref="Build"/> 합니다.
	/// </summary>
	/// <example>
	/// <code>
	/// var client = new HttpClientBuilder()
	///     .WithBaseAddress("https://api.example.com/")
	///     .AddMiddleware(new TimeoutMiddleware(TimeSpan.FromSeconds(10)))
	///     .AddMiddleware(new RetryMiddleware())
	///     .Build();
	/// </code>
	/// </example>
	public sealed class HttpClientBuilder
	{
		private readonly List<(int Order, int Index, IHttpMiddleware Middleware)> _middlewares = new List<(int, int, IHttpMiddleware)>();
		private Uri _baseAddress;
		private IHttpTransport _transport;
		private JsonSerializerSettings _jsonSettings;

		/// <summary>상대 주소의 기준입니다. 경로를 이어 붙이려면 끝을 <c>/</c> 로 맞춥니다(<c>https://host/api/</c> + <c>users</c>).</summary>
		/// <exception cref="ArgumentException">절대 주소가 아닌 경우</exception>
		public HttpClientBuilder WithBaseAddress(string baseAddress)
		{
			if (string.IsNullOrEmpty(baseAddress) || !Uri.TryCreate(baseAddress, UriKind.Absolute, out var uri))
			{
				throw new ArgumentException($"Base address must be an absolute uri: '{baseAddress}'", nameof(baseAddress));
			}

			_baseAddress = uri;
			return this;
		}

		/// <summary>
		/// 미들웨어를 추가합니다. order 가 작을수록 바깥(먼저 실행)이고, 같으면 추가한 순서입니다.
		/// </summary>
		/// <exception cref="ArgumentNullException"><paramref name="middleware"/> 가 null 인 경우</exception>
		public HttpClientBuilder AddMiddleware(IHttpMiddleware middleware, int order = 0)
		{
			_middlewares.Add((order, _middlewares.Count, middleware ?? throw new ArgumentNullException(nameof(middleware))));
			return this;
		}

		/// <summary>전송을 바꿉니다. 기본은 <see cref="UnityWebRequestTransport"/> 입니다.</summary>
		public HttpClientBuilder WithTransport(IHttpTransport transport)
		{
			_transport = transport ?? throw new ArgumentNullException(nameof(transport));
			return this;
		}

		/// <summary>JSON 직렬화 설정입니다. 기본은 Newtonsoft 기본값입니다.</summary>
		public HttpClientBuilder WithJsonSettings(JsonSerializerSettings settings)
		{
			_jsonSettings = settings ?? throw new ArgumentNullException(nameof(settings));
			return this;
		}

		public HttpClient Build()
		{
			var ordered = _middlewares
				.OrderBy(entry => entry.Order)
				.ThenBy(entry => entry.Index)
				.Select(entry => entry.Middleware)
				.ToArray();

			return new HttpClient(_baseAddress, ordered, _transport ?? new UnityWebRequestTransport(), _jsonSettings);
		}
	}
}
