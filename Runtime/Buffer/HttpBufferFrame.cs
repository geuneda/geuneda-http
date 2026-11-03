using System;
using System.Text;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// 네이티브 전송에 넘기는 요청 바이트 형식입니다. Java <c>NativeHttpChannel.parse</c> 와 짝이고, 바꾸면 두 쪽과 두 테스트(같은 기준 바이트)를 함께 바꿉니다.
	/// </summary>
	/// <remarks>
	/// 정수는 4바이트 빅 엔디언, 문자열은 UTF-8 바이트 수 + 바이트입니다.
	/// <c>버전(1) · 메서드 · 제한 시간 ms(0 = 없음) · 주소 · Content-Type(본문 없으면 빈 문자열) · 헤더 수 · (이름 · 값)* · 본문 길이(-1 = 본문 없음) · 본문</c>.
	/// 본문이 있으면 헤더의 Content-Type 은 건너뜁니다(<see cref="UnityWebRequestTransport"/> 와 같습니다).
	/// </remarks>
	internal static class HttpBufferFrame
	{
		public const int Version = 1;

		private const string ContentTypeHeader = "Content-Type";

		/// <summary>요청을 <paramref name="buffer"/> 앞에 쓰고 길이를 돌려줍니다. 모자라면 버퍼를 키웁니다.</summary>
		/// <exception cref="ArgumentException">주소가 http(s) 절대 주소가 아닌 경우</exception>
		public static int Write(HttpBufferRequest request, ref byte[] buffer)
		{
			if (!IsAbsoluteHttpUrl(request.Url))
			{
				throw new ArgumentException($"Transport needs an absolute http(s) url: {request.Url}", nameof(request));
			}

			var skipContentType = request.HasBody;
			// 정수 7개: 버전, 메서드 길이, 제한 시간, 주소 길이, Content-Type 길이, 헤더 수, 본문 길이
			var size = 4 * 7 +Encoding.UTF8.GetByteCount(request.Method.ToVerb()) + Encoding.UTF8.GetByteCount(request.Url)
				+ (request.HasBody ? Encoding.UTF8.GetByteCount(request.ContentType) + request.BodyLength : 0);

			for (var i = 0; i < request.HeaderCount; i++)
			{
				if (skipContentType && IsContentType(request.GetHeaderName(i)))
				{
					continue;
				}

				size += 8 + Encoding.UTF8.GetByteCount(request.GetHeaderName(i)) + Encoding.UTF8.GetByteCount(request.GetHeaderValue(i));
			}

			if (buffer == null || buffer.Length < size)
			{
				// 딱 맞게 키우면 본문이 조금 커질 때 다시 키운다. 반만큼 여유를 둔다
				buffer = new byte[Math.Max(size + size / 2, buffer == null ? 0 : buffer.Length * 2)];
			}

			var at = 0;
			WriteInt(buffer, ref at, Version);
			WriteString(buffer, ref at, request.Method.ToVerb());
			WriteInt(buffer, ref at, TimeoutMilliseconds(request.Timeout));
			WriteString(buffer, ref at, request.Url);
			WriteString(buffer, ref at, request.HasBody ? request.ContentType : "");

			var countAt = at;
			var count = 0;
			at += 4;

			for (var i = 0; i < request.HeaderCount; i++)
			{
				if (skipContentType && IsContentType(request.GetHeaderName(i)))
				{
					continue;
				}

				WriteString(buffer, ref at, request.GetHeaderName(i));
				WriteString(buffer, ref at, request.GetHeaderValue(i));
				count++;
			}

			WriteInt(buffer, ref countAt, count);

			if (!request.HasBody)
			{
				WriteInt(buffer, ref at, -1);
				return at;
			}

			WriteInt(buffer, ref at, request.BodyLength);
			request.Body.CopyTo(new Span<byte>(buffer, at, request.BodyLength));
			return at + request.BodyLength;
		}

		internal static bool IsAbsoluteHttpUrl(string url)
		{
			if (url == null)
			{
				return false;
			}

			var span = url.AsSpan();
			return span.StartsWith("https://".AsSpan(), StringComparison.OrdinalIgnoreCase) && span.Length > 8
				|| span.StartsWith("http://".AsSpan(), StringComparison.OrdinalIgnoreCase) && span.Length > 7;
		}

		private static int TimeoutMilliseconds(TimeSpan? timeout)
		{
			if (!timeout.HasValue)
			{
				return 0;
			}

			// 1ms 보다 짧아도 0(없음)이 되지 않게 올린다
			return (int)Math.Min(int.MaxValue, Math.Max(1, Math.Ceiling(timeout.Value.TotalMilliseconds)));
		}

		private static bool IsContentType(string name) => string.Equals(name, ContentTypeHeader, StringComparison.OrdinalIgnoreCase);

		private static void WriteString(byte[] buffer, ref int at, string value)
		{
			var length = Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, at + 4);
			WriteInt(buffer, ref at, length);
			at += length;
		}

		private static void WriteInt(byte[] buffer, ref int at, int value)
		{
			buffer[at] = (byte)(value >> 24);
			buffer[at + 1] = (byte)(value >> 16);
			buffer[at + 2] = (byte)(value >> 8);
			buffer[at + 3] = (byte)value;
			at += 4;
		}
	}
}
