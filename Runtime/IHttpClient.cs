using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// HTTP 클라이언트입니다. 기본 구현은 <see cref="HttpClient"/> 이고(<see cref="HttpClientBuilder"/> 로 만듦), services 의 <c>MainInstaller</c> 에 이 인터페이스로 바인딩합니다.
	/// </summary>
	/// <remarks>
	/// 모든 메서드는 실패를 예외 대신 <c>Result</c> 코드로 돌려줍니다(core 결과 규약). 취소도 <c>ResultCodes.Cancelled</c> 입니다.
	/// 잘못된 주소처럼 호출자가 고쳐야 하는 오용은 예외입니다.
	/// </remarks>
	public interface IHttpClient
	{
		/// <summary>미들웨어 체인을 거쳐 요청을 보냅니다. 4xx, 5xx 도 성공한 결과의 응답으로 옵니다.</summary>
		UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, CancellationToken cancellationToken = default);

		/// <summary>요청을 보내고 2xx 응답 본문을 JSON 으로 읽습니다. 2xx 가 아니면 <see cref="HttpResultCodes.HttpError"/> 입니다.</summary>
		UniTask<Result<T>> SendJsonAsync<T>(HttpRequest request, CancellationToken cancellationToken = default);

		UniTask<Result<HttpResponse>> GetAsync(string uri, CancellationToken cancellationToken = default);

		UniTask<Result<T>> GetJsonAsync<T>(string uri, CancellationToken cancellationToken = default);

		/// <summary><paramref name="body"/> 를 JSON 으로 보냅니다.</summary>
		UniTask<Result<HttpResponse>> PostJsonAsync(string uri, object body, CancellationToken cancellationToken = default);

		/// <summary><paramref name="body"/> 를 JSON 으로 보내고 2xx 응답 본문을 JSON 으로 읽습니다.</summary>
		UniTask<Result<T>> PostJsonAsync<T>(string uri, object body, CancellationToken cancellationToken = default);
	}
}
