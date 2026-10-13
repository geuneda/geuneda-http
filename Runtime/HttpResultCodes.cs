// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// http 패키지의 결과 코드입니다(core 결과 코드 범위 표의 1000 블록).
	/// </summary>
	/// <remarks>
	/// 연결 실패, 타임아웃, 취소는 core 공용 코드(<c>ResultCodes.NetworkError</c>, <c>Timeout</c>, <c>Cancelled</c>)를 씁니다.
	/// </remarks>
	public static class HttpResultCodes
	{
		/// <summary>이 패키지 코드 블록의 시작입니다. 코드로는 쓰지 않습니다.</summary>
		public const int RangeStart = 1000;
		/// <summary>응답은 왔지만 상태 코드가 2xx 가 아닙니다. JSON 편의 메서드에서만 씁니다.</summary>
		public const int HttpError = RangeStart + 1;
		/// <summary>응답 본문을 읽거나 해석하지 못했습니다(비어 있음, JSON 형식 오류, 내려받기 처리 오류).</summary>
		public const int InvalidResponse = RangeStart + 2;
	}
}
