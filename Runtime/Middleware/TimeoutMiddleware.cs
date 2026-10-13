using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// 제한 시간이 없는 요청에 기본 제한 시간을 붙입니다. 요청에 직접 정한 값(<see cref="HttpRequest.WithTimeout"/>)은 바꾸지 않습니다.
	/// </summary>
	/// <remarks>
	/// 제한 시간은 전송 한 번에 적용됩니다. <see cref="RetryMiddleware"/> 와 함께 쓰면 시도마다 따로 적용됩니다.
	/// 넘으면 전송이 요청을 중단하고 <c>ResultCodes.Timeout</c> 을 돌려줍니다.
	/// </remarks>
	public sealed class TimeoutMiddleware : IHttpMiddleware
	{
		public TimeSpan Timeout { get; }

		/// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> 이 0 이하인 경우</exception>
		public TimeoutMiddleware(TimeSpan timeout)
		{
			if (timeout <= TimeSpan.Zero)
			{
				throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be positive");
			}

			Timeout = timeout;
		}

		public UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, HttpHandler next, CancellationToken cancellationToken)
		{
			return next(request.Timeout.HasValue ? request : request.WithTimeout(Timeout), cancellationToken);
		}
	}
}
