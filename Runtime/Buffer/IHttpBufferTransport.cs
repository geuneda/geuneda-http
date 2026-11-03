using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// 다시 쓰는 요청·응답 객체로 보내는 전송입니다. 120초 동기화처럼 반복 요청에서 C# 할당을 없앨 때 씁니다.
	/// 기본 구현은 <see cref="HttpBufferTransports.CreateDefault"/> 가 고릅니다.
	/// </summary>
	/// <remarks>
	/// 결과 규약은 <see cref="IHttpTransport"/> 와 같습니다. 응답이 오면(4xx, 5xx 포함) 성공 <c>Result</c> 이고 <paramref name="response"/> 에
	/// 상태 코드와 본문이 들어갑니다. 응답이 없으면 연결 실패 <c>NetworkError</c>, 제한 시간 초과 <c>Timeout</c>, 호출자 취소 <c>Cancelled</c> 입니다.
	/// 미들웨어·기본 주소·재시도는 거치지 않습니다. 끝날 때까지 두 객체를 바꾸거나 다른 요청에 넘기지 않습니다.
	/// 만든 쪽이 <see cref="IDisposable.Dispose"/> 로 닫습니다(네이티브 구현은 직접 버퍼·JNI 참조를 가집니다). 메인 스레드에서 닫습니다.
	/// </remarks>
	public interface IHttpBufferTransport : IDisposable
	{
		/// <exception cref="System.ArgumentNullException">요청이나 응답이 null 인 경우</exception>
		/// <exception cref="System.ArgumentException">주소가 http(s) 절대 주소가 아니거나 형식이 틀린 경우, 헤더가 잘못된 경우</exception>
		UniTask<Result> SendAsync(HttpBufferRequest request, HttpBufferResponse response, CancellationToken cancellationToken);
	}
}
