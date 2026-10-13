using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// 체인의 다음 단계(다음 미들웨어 또는 전송)입니다. 여러 번 불러도 됩니다(재시도).
	/// </summary>
	public delegate UniTask<Result<HttpResponse>> HttpHandler(HttpRequest request, CancellationToken cancellationToken);

	/// <summary>
	/// 요청과 응답 사이에 끼는 단계입니다. 헤더 추가, 토큰 재발급 후 재시도, 응답 바꾸기 같은 공통 처리를 둡니다.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 요청을 바꾸려면 <c>With*</c> 로 복사본을 만들어 <paramref name="next"/> 에 넘깁니다. 같은 요청을 다시 보내려면 <c>next</c> 를 다시 부릅니다.
	/// </para>
	/// <para>
	/// 4xx, 5xx 는 성공한 <c>Result</c> 의 <see cref="HttpResponse.StatusCode"/> 로 옵니다. 연결 실패, 타임아웃, 취소는 실패한 <c>Result</c>
	/// (<c>ResultCodes.NetworkError</c>, <c>Timeout</c>, <c>Cancelled</c>)입니다. 취소된 결과는 재시도하지 않습니다.
	/// </para>
	/// </remarks>
	public interface IHttpMiddleware
	{
		UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, HttpHandler next, CancellationToken cancellationToken);
	}
}
