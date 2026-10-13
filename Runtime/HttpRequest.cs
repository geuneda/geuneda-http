using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Newtonsoft.Json;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// HTTP 요청입니다. 바꾸지 않는 객체이고, <c>With*</c> 메서드가 바뀐 복사본을 돌려줍니다.
	/// 미들웨어는 복사본을 만들어 다음 단계에 넘깁니다.
	/// </summary>
	public sealed class HttpRequest
	{
		// 바꿀 수 없는 빈 사전이라 요청끼리 공유해도 된다
		private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
			new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

		public HttpMethod Method { get; }

		/// <summary>요청 주소입니다. 상대 주소면 <see cref="HttpClient"/> 가 보내기 전에 기본 주소와 합칩니다.</summary>
		public Uri Uri { get; }

		/// <summary>요청 헤더입니다. 이름은 대소문자를 가리지 않습니다.</summary>
		public IReadOnlyDictionary<string, string> Headers { get; }

		/// <summary>본문입니다. 없으면 null 입니다. 넘긴 배열을 바꾸지 마세요.</summary>
		public byte[] Body { get; }

		/// <summary>본문의 Content-Type 입니다. 본문이 없으면 null 입니다.</summary>
		public string ContentType { get; }

		/// <summary>요청 한 번의 제한 시간입니다. 없으면 null(제한 없음)입니다.</summary>
		public TimeSpan? Timeout { get; }

		/// <exception cref="ArgumentNullException"><paramref name="uri"/> 가 null 인 경우</exception>
		/// <exception cref="ArgumentOutOfRangeException"><paramref name="method"/> 가 정의되지 않은 값인 경우</exception>
		public HttpRequest(HttpMethod method, Uri uri)
			: this(ValidateMethod(method), uri ?? throw new ArgumentNullException(nameof(uri)), EmptyHeaders, null, null, null)
		{
		}

		/// <param name="uri">절대 주소 또는 기본 주소에 대한 상대 주소</param>
		/// <exception cref="ArgumentException"><paramref name="uri"/> 가 비어 있거나 주소 형식이 아닌 경우</exception>
		public HttpRequest(HttpMethod method, string uri) : this(method, ParseUri(uri))
		{
		}

		private HttpRequest(HttpMethod method, Uri uri, IReadOnlyDictionary<string, string> headers, byte[] body, string contentType, TimeSpan? timeout)
		{
			Method = method;
			Uri = uri;
			Headers = headers;
			Body = body;
			ContentType = contentType;
			Timeout = timeout;
		}

		public static HttpRequest Get(string uri) => new HttpRequest(HttpMethod.Get, uri);
		public static HttpRequest Post(string uri) => new HttpRequest(HttpMethod.Post, uri);
		public static HttpRequest Put(string uri) => new HttpRequest(HttpMethod.Put, uri);
		public static HttpRequest Patch(string uri) => new HttpRequest(HttpMethod.Patch, uri);
		public static HttpRequest Delete(string uri) => new HttpRequest(HttpMethod.Delete, uri);

		/// <summary>헤더를 넣거나 같은 이름의 값을 바꾼 복사본입니다.</summary>
		/// <exception cref="ArgumentException"><paramref name="name"/> 이 비어 있는 경우</exception>
		/// <exception cref="ArgumentNullException"><paramref name="value"/> 가 null 인 경우</exception>
		public HttpRequest WithHeader(string name, string value)
		{
			if (string.IsNullOrEmpty(name))
			{
				throw new ArgumentException("Header name must not be empty", nameof(name));
			}

			if (value == null)
			{
				throw new ArgumentNullException(nameof(value));
			}

			var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			foreach (var header in Headers)
			{
				headers[header.Key] = header.Value;
			}

			headers[name] = value;
			return new HttpRequest(Method, Uri, new ReadOnlyDictionary<string, string>(headers), Body, ContentType, Timeout);
		}

		/// <summary>본문과 Content-Type 을 바꾼 복사본입니다.</summary>
		/// <exception cref="ArgumentNullException"><paramref name="body"/> 나 <paramref name="contentType"/> 이 null 인 경우</exception>
		public HttpRequest WithBody(byte[] body, string contentType)
		{
			return new HttpRequest(Method, Uri, Headers,
				body ?? throw new ArgumentNullException(nameof(body)),
				contentType ?? throw new ArgumentNullException(nameof(contentType)),
				Timeout);
		}

		/// <summary><paramref name="body"/> 를 JSON(UTF-8, <c>application/json</c>)으로 직렬화한 본문의 복사본입니다.</summary>
		public HttpRequest WithJsonBody(object body, JsonSerializerSettings settings = null)
		{
			var json = JsonConvert.SerializeObject(body, settings);
			return WithBody(Encoding.UTF8.GetBytes(json), "application/json");
		}

		/// <summary>제한 시간을 바꾼 복사본입니다.</summary>
		/// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> 이 0 이하인 경우</exception>
		public HttpRequest WithTimeout(TimeSpan timeout)
		{
			if (timeout <= TimeSpan.Zero)
			{
				throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be positive");
			}

			return new HttpRequest(Method, Uri, Headers, Body, ContentType, timeout);
		}

		/// <summary>주소를 바꾼 복사본입니다.</summary>
		/// <exception cref="ArgumentNullException"><paramref name="uri"/> 가 null 인 경우</exception>
		public HttpRequest WithUri(Uri uri)
		{
			return new HttpRequest(Method, uri ?? throw new ArgumentNullException(nameof(uri)), Headers, Body, ContentType, Timeout);
		}

		public override string ToString()
		{
			return $"{Method.ToVerb()} {Uri}";
		}

		private static HttpMethod ValidateMethod(HttpMethod method)
		{
			if (!Enum.IsDefined(typeof(HttpMethod), method))
			{
				throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown HTTP method");
			}

			return method;
		}

		private static Uri ParseUri(string uri)
		{
			if (string.IsNullOrEmpty(uri) || !Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out var parsed))
			{
				throw new ArgumentException($"Invalid request uri '{uri}'", nameof(uri));
			}

			return parsed;
		}
	}
}
