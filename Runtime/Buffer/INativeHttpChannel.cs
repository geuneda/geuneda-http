using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// 네이티브 요청 하나를 보내는 통로(Java <c>NativeHttpChannel</c>)입니다. 메인 스레드에서만 부릅니다.
	/// 테스트에서는 가짜로 바꿉니다.
	/// </summary>
	/// <remarks>
	/// <see cref="Start"/> 는 프레임을 네이티브 쪽에 복사한 뒤 돌아오므로, 그 뒤에 프레임 버퍼를 다시 써도 됩니다.
	/// <see cref="Cancel"/> 뒤에는 그 요청이 끝나길 기다리지 않고 바로 다음 <see cref="Start"/> 를 불러도 됩니다.
	/// </remarks>
	internal interface INativeHttpChannel : IDisposable
	{
		void Start(byte[] frame, int length);

		/// <summary><see cref="NativeHttpState"/> 값입니다.</summary>
		int Poll();

		int StatusCode { get; }

		int ResponseLength { get; }

		/// <summary>응답 본문 앞 <paramref name="length"/> 바이트를 복사합니다.</summary>
		void CopyResponse(byte[] destination, int length);

		/// <summary>실패 이유입니다(실패했을 때만 부릅니다, 할당합니다).</summary>
		string Error { get; }

		void Cancel();
	}

	/// <summary>Java <c>NativeHttpChannel</c> 의 상태 값과 같습니다.</summary>
	internal static class NativeHttpState
	{
		public const int Idle = 0;
		public const int Running = 1;
		public const int Done = 2;
		public const int NetworkError = 3;
		public const int Timeout = 4;
		public const int Cancelled = 5;
		public const int InvalidRequest = 6;
	}
}
