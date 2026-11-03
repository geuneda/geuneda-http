using System;
using System.Text;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// <see cref="IHttpBufferTransport"/> 가 채우는 다시 쓰는 응답입니다. 본문 버퍼는 커질 때만 새로 잡습니다.
	/// </summary>
	/// <remarks>
	/// 다음 요청에 같은 객체를 넘기면 내용이 덮입니다. 응답 헤더는 담지 않습니다(필요하면 <see cref="IHttpClient"/> 를 씁니다).
	/// </remarks>
	public sealed class HttpBufferResponse
	{
		private byte[] _buffer;

		public HttpBufferResponse(int initialCapacity = 16 * 1024)
		{
			if (initialCapacity < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(initialCapacity));
			}

			_buffer = new byte[initialCapacity];
		}

		/// <summary>마지막으로 받은 응답의 상태 코드입니다. 응답이 없었으면 0 입니다.</summary>
		public int StatusCode { get; private set; }

		public bool IsSuccessStatusCode => StatusCode >= 200 && StatusCode <= 299;

		public int Length { get; private set; }

		public ReadOnlySpan<byte> Body => new ReadOnlySpan<byte>(_buffer, 0, Length);

		/// <summary>본문을 담은 버퍼입니다. 앞 <see cref="Length"/> 바이트만 이번 응답입니다.</summary>
		public byte[] Buffer => _buffer;

		/// <summary>본문을 UTF-8 문자열로 읽습니다(할당합니다).</summary>
		public string ReadText() => Encoding.UTF8.GetString(_buffer, 0, Length);

		/// <summary>전송이 응답을 쓰기 전에 부릅니다. 길이만큼 쓸 수 있는 버퍼를 돌려줍니다.</summary>
		internal byte[] Begin(int statusCode, int length)
		{
			if (length < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(length));
			}

			if (_buffer.Length < length)
			{
				_buffer = new byte[Math.Max(length + length / 2, _buffer.Length * 2)];
			}

			StatusCode = statusCode;
			Length = length;
			return _buffer;
		}

		internal void Reset()
		{
			StatusCode = 0;
			Length = 0;
		}
	}
}
