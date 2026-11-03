using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// <see cref="IHttpBufferTransport"/> 로 보내는 다시 쓰는 요청입니다. 주기 요청처럼 할당 없이 보내야 할 때 씁니다.
	/// </summary>
	/// <remarks>
	/// <see cref="HttpRequest"/> 와 달리 바뀌는 객체입니다. 보내는 동안(<c>SendAsync</c> 가 끝날 때까지) 바꾸지 않습니다.
	/// 헤더와 본문 버퍼는 커질 때만 새로 잡습니다. 주소는 절대 주소이고 기본 주소·미들웨어를 거치지 않습니다.
	/// </remarks>
	public sealed class HttpBufferRequest
	{
		private string[] _headerNames = new string[8];
		private string[] _headerValues = new string[8];
		private byte[] _body = Array.Empty<byte>();
		private HttpMethod _method = HttpMethod.Post;
		private TimeSpan? _timeout;

		/// <summary>기본은 POST 입니다.</summary>
		/// <exception cref="ArgumentOutOfRangeException">정의되지 않은 값인 경우</exception>
		public HttpMethod Method
		{
			get => _method;
			set
			{
				value.ToVerb();
				_method = value;
			}
		}

		/// <summary>절대 주소입니다. 전송이 보내기 전에 확인합니다.</summary>
		public string Url { get; set; }

		/// <summary>null 이면 제한 시간이 없습니다.</summary>
		/// <exception cref="ArgumentOutOfRangeException">0 이하인 경우</exception>
		public TimeSpan? Timeout
		{
			get => _timeout;
			set
			{
				if (value.HasValue && value.Value <= TimeSpan.Zero)
				{
					throw new ArgumentOutOfRangeException(nameof(value), value, "Timeout must be positive");
				}

				_timeout = value;
			}
		}

		public int HeaderCount { get; private set; }

		/// <summary><see cref="SetBody"/> 를 불렀으면 true, <see cref="ClearBody"/> 뒤에는 false 입니다.</summary>
		public bool HasBody { get; private set; }

		public string ContentType { get; private set; }

		public ReadOnlySpan<byte> Body => new ReadOnlySpan<byte>(_body, 0, BodyLength);

		public int BodyLength { get; private set; }

		public string GetHeaderName(int index) => _headerNames[CheckHeaderIndex(index)];

		public string GetHeaderValue(int index) => _headerValues[CheckHeaderIndex(index)];

		/// <summary>헤더를 넣거나 같은 이름(대소문자 무시)의 값을 바꿉니다.</summary>
		/// <exception cref="ArgumentException"><paramref name="name"/> 이 비어 있는 경우</exception>
		/// <exception cref="ArgumentNullException"><paramref name="value"/> 가 null 인 경우</exception>
		public void SetHeader(string name, string value)
		{
			if (string.IsNullOrEmpty(name))
			{
				throw new ArgumentException("Header name must not be empty", nameof(name));
			}

			if (value == null)
			{
				throw new ArgumentNullException(nameof(value));
			}

			var index = IndexOfHeader(name);

			if (index < 0)
			{
				if (HeaderCount == _headerNames.Length)
				{
					Array.Resize(ref _headerNames, HeaderCount * 2);
					Array.Resize(ref _headerValues, HeaderCount * 2);
				}

				index = HeaderCount++;
				_headerNames[index] = name;
			}

			_headerValues[index] = value;
		}

		/// <summary>헤더를 뺍니다. 없으면 아무것도 하지 않습니다.</summary>
		public void RemoveHeader(string name)
		{
			var index = IndexOfHeader(name);

			if (index < 0)
			{
				return;
			}

			HeaderCount--;
			_headerNames[index] = _headerNames[HeaderCount];
			_headerValues[index] = _headerValues[HeaderCount];
			_headerNames[HeaderCount] = null;
			_headerValues[HeaderCount] = null;
		}

		public void ClearHeaders()
		{
			Array.Clear(_headerNames, 0, HeaderCount);
			Array.Clear(_headerValues, 0, HeaderCount);
			HeaderCount = 0;
		}

		/// <summary>본문을 내부 버퍼에 복사합니다. 헤더의 Content-Type 보다 <paramref name="contentType"/> 이 이깁니다.</summary>
		/// <exception cref="ArgumentNullException"><paramref name="contentType"/> 이 null 인 경우</exception>
		public void SetBody(ReadOnlySpan<byte> body, string contentType)
		{
			ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));

			if (_body.Length < body.Length)
			{
				_body = new byte[Math.Max(body.Length + body.Length / 2, _body.Length * 2)];
			}

			body.CopyTo(_body);
			BodyLength = body.Length;
			HasBody = true;
		}

		public void ClearBody()
		{
			ContentType = null;
			BodyLength = 0;
			HasBody = false;
		}

		public override string ToString() => $"{_method.ToVerb()} {Url}";

		private int IndexOfHeader(string name)
		{
			for (var i = 0; i < HeaderCount; i++)
			{
				if (string.Equals(_headerNames[i], name, StringComparison.OrdinalIgnoreCase))
				{
					return i;
				}
			}

			return -1;
		}

		private int CheckHeaderIndex(int index)
		{
			if ((uint)index >= (uint)HeaderCount)
			{
				throw new ArgumentOutOfRangeException(nameof(index));
			}

			return index;
		}
	}
}
