using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Geuneda.Core;
using Newtonsoft.Json;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// 서버가 돌려준 HTTP 응답입니다. 4xx, 5xx 도 응답입니다(<see cref="IsSuccessStatusCode"/> 로 구분).
	/// 응답 자체가 없는 실패(연결 실패, 타임아웃, 취소)는 응답이 아니라 실패한 <c>Result</c> 로 옵니다.
	/// </summary>
	public sealed class HttpResponse
	{
		private static readonly byte[] EmptyBody = new byte[0];

		public Uri Uri { get; }

		public int StatusCode { get; }

		/// <summary>응답 본문입니다. 없으면 빈 배열입니다.</summary>
		public byte[] Body { get; }

		/// <summary>응답 헤더입니다. 이름은 대소문자를 가리지 않습니다.</summary>
		public IReadOnlyDictionary<string, string> Headers { get; }

		public bool IsSuccessStatusCode => StatusCode >= 200 && StatusCode <= 299;

		public HttpResponse(Uri uri, int statusCode, byte[] body, IReadOnlyDictionary<string, string> headers)
		{
			Uri = uri ?? throw new ArgumentNullException(nameof(uri));
			StatusCode = statusCode;
			Body = body ?? EmptyBody;

			var copied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			if (headers != null)
			{
				foreach (var header in headers)
				{
					copied[header.Key] = header.Value;
				}
			}

			Headers = new ReadOnlyDictionary<string, string>(copied);
		}

		/// <summary>본문을 UTF-8 문자열로 읽습니다.</summary>
		public string ReadText()
		{
			return Encoding.UTF8.GetString(Body);
		}

		/// <summary>
		/// 본문을 JSON 으로 읽습니다. 본문이 비었거나 공백뿐이거나 JSON 으로 읽지 못하면 <see cref="HttpResultCodes.InvalidResponse"/> 입니다.
		/// 본문이 JSON 리터럴 <c>null</c> 이면 참조 형식과 nullable 값 형식은 성공이고 값은 null 입니다. <c>int</c> 같은 nullable 이 아닌 값 형식은
		/// 변환할 수 없어 <see cref="HttpResultCodes.InvalidResponse"/> 입니다(Newtonsoft 동작). 상태 코드는 보지 않습니다.
		/// </summary>
		public Result<T> ReadJson<T>(JsonSerializerSettings settings = null)
		{
			var text = ReadText();

			if (string.IsNullOrWhiteSpace(text))
			{
				return Result<T>.Fail(HttpResultCodes.InvalidResponse, $"Empty response body from {Uri}");
			}

			try
			{
				return Result<T>.Ok(JsonConvert.DeserializeObject<T>(text, settings));
			}
			catch (JsonException exception)
			{
				return Result<T>.Fail(HttpResultCodes.InvalidResponse, $"Invalid JSON from {Uri}: {exception.Message}");
			}
		}

		public override string ToString()
		{
			return $"{StatusCode} {Uri}";
		}
	}
}
