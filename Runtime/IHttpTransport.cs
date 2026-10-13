using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// 실제로 요청을 보내는 마지막 단계입니다. 기본은 <see cref="UnityWebRequestTransport"/> 이고, 테스트에서는 가짜로 바꿉니다.
	/// </summary>
	/// <remarks>
	/// 받은 요청의 <see cref="HttpRequest.Uri"/> 는 절대 주소입니다. 응답이 오면(4xx, 5xx 포함) 성공 <c>Result</c>, 응답이 없으면
	/// 연결 실패 <c>NetworkError</c>, 제한 시간 초과 <c>Timeout</c>, 호출자 취소 <c>Cancelled</c> 로 돌려줍니다.
	/// </remarks>
	public interface IHttpTransport
	{
		UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, CancellationToken cancellationToken);
	}
}
