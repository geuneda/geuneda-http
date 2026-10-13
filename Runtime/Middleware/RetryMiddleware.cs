using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// 멱등 요청(GET, HEAD, PUT, DELETE)이 일시적인 이유로 실패하면 지수 백오프와 지터를 두고 다시 보냅니다.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 다시 보내는 경우: 연결 실패(<c>ResultCodes.NetworkError</c>), 타임아웃(<c>ResultCodes.Timeout</c>), 상태 코드 408, 429, 500, 502, 503, 504.
	/// 취소, 그 밖의 4xx, POST·PATCH 는 다시 보내지 않습니다. 마지막 시도의 결과를 돌려줍니다.
	/// </para>
	/// <para>
	/// n 번째 재시도(0부터) 전 대기 시간은 <c>min(maxDelay, baseDelay × 2^n)</c> 의 50~100% 사이 무작위 값입니다. 기다리는 중에 취소되면 <c>Cancelled</c> 입니다.
	/// <c>Retry-After</c> 헤더는 보지 않습니다.
	/// </para>
	/// </remarks>
	public sealed class RetryMiddleware : IHttpMiddleware
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.Http.Retry");

		private readonly int _maxRetries;
		private readonly TimeSpan _baseDelay;
		private readonly TimeSpan _maxDelay;
		private readonly Func<TimeSpan, CancellationToken, UniTask> _delay;
		private readonly Func<double> _random;

		/// <param name="maxRetries">처음 요청 뒤 다시 보내는 최대 횟수(0 이상)</param>
		/// <param name="baseDelay">첫 재시도의 대기 상한. 기본 0.5초</param>
		/// <param name="maxDelay">대기 상한의 최댓값. 기본 8초</param>
		/// <exception cref="ArgumentOutOfRangeException">값이 음수이거나 baseDelay 가 maxDelay 보다 큰 경우</exception>
		public RetryMiddleware(int maxRetries = 3, TimeSpan? baseDelay = null, TimeSpan? maxDelay = null)
			: this(maxRetries, baseDelay ?? TimeSpan.FromSeconds(0.5), maxDelay ?? TimeSpan.FromSeconds(8), DefaultDelay, CreateDefaultRandom())
		{
		}

		internal RetryMiddleware(int maxRetries, TimeSpan baseDelay, TimeSpan maxDelay, Func<TimeSpan, CancellationToken, UniTask> delay, Func<double> random)
		{
			if (maxRetries < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(maxRetries), maxRetries, "Must not be negative");
			}

			if (baseDelay < TimeSpan.Zero || maxDelay < baseDelay)
			{
				throw new ArgumentOutOfRangeException(nameof(baseDelay), baseDelay, "Delays must be non-negative and baseDelay <= maxDelay");
			}

			_maxRetries = maxRetries;
			_baseDelay = baseDelay;
			_maxDelay = maxDelay;
			_delay = delay;
			_random = random;
		}

		public async UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, HttpHandler next, CancellationToken cancellationToken)
		{
			var result = await next(request, cancellationToken);

			for (var attempt = 0; attempt < _maxRetries && ShouldRetry(request, result); attempt++)
			{
				var delay = GetDelay(attempt);
				Logger.Debug($"Retrying {request} in {delay.TotalSeconds:0.###}s after {Describe(result)}");

				try
				{
					await _delay(delay, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					return Result<HttpResponse>.Fail(ResultCodes.Cancelled);
				}

				result = await next(request, cancellationToken);
			}

			return result;
		}

		/// <summary>
		/// <paramref name="attempt"/> 번째 재시도(0부터) 전 대기 시간입니다.
		/// </summary>
		internal TimeSpan GetDelay(int attempt)
		{
			var exponential = _baseDelay.TotalMilliseconds * Math.Pow(2, Math.Min(attempt, 30));
			var cap = Math.Min(_maxDelay.TotalMilliseconds, exponential);

			return TimeSpan.FromMilliseconds(cap * (0.5 + 0.5 * _random()));
		}

		internal static bool ShouldRetry(HttpRequest request, Result<HttpResponse> result)
		{
			if (!request.Method.IsIdempotent())
			{
				return false;
			}

			if (!result.TryGetValue(out var response))
			{
				return result.Code == ResultCodes.NetworkError || result.Code == ResultCodes.Timeout;
			}

			switch (response.StatusCode)
			{
				case 408:
				case 429:
				case 500:
				case 502:
				case 503:
				case 504:
					return true;
				default:
					return false;
			}
		}

		private static string Describe(Result<HttpResponse> result)
		{
			return result.TryGetValue(out var response) ? $"HTTP {response.StatusCode}" : result.ToString();
		}

		private static UniTask DefaultDelay(TimeSpan delay, CancellationToken cancellationToken)
		{
			return UniTask.Delay(delay, true, PlayerLoopTiming.Update, cancellationToken);
		}

		private static Func<double> CreateDefaultRandom()
		{
			var random = new Random();
			var gate = new object();

			return () =>
			{
				lock (gate)
				{
					return random.NextDouble();
				}
			};
		}
	}
}
