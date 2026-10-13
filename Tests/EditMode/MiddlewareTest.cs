using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http.Tests
{
	[TestFixture]
	public class MiddlewareTest
	{
		private FakeTransport _transport;
		private List<TimeSpan> _delays;

		[SetUp]
		public void SetUp()
		{
			_transport = new FakeTransport();
			_delays = new List<TimeSpan>();
		}

		private RetryMiddleware Retry(int maxRetries = 3, double random = 1.0)
		{
			return new RetryMiddleware(maxRetries, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(8),
				(delay, cancellationToken) =>
				{
					_delays.Add(delay);
					return UniTask.CompletedTask;
				},
				() => random);
		}

		private HttpClient Client(params IHttpMiddleware[] middlewares)
		{
			var builder = new HttpClientBuilder().WithBaseAddress("https://api.example.com/").WithTransport(_transport);

			foreach (var middleware in middlewares)
			{
				builder.AddMiddleware(middleware);
			}

			return builder.Build();
		}

		private Result<HttpResponse> Send(HttpClient client, HttpRequest request, CancellationToken cancellationToken = default)
		{
			return FakeTransport.Run(client.SendAsync(request, cancellationToken));
		}

		[Test]
		public void Timeout_AddedWhenMissing_KeptWhenSet()
		{
			var client = Client(new TimeoutMiddleware(TimeSpan.FromSeconds(10)));

			Send(client, HttpRequest.Get("a"));
			Send(client, HttpRequest.Get("b").WithTimeout(TimeSpan.FromSeconds(2)));

			Assert.AreEqual(TimeSpan.FromSeconds(10), _transport.Requests[0].Timeout);
			Assert.AreEqual(TimeSpan.FromSeconds(2), _transport.Requests[1].Timeout);
			Assert.Throws<ArgumentOutOfRangeException>(() => new TimeoutMiddleware(TimeSpan.Zero));
		}

		[Test]
		public void DefaultHeaders_AddedWhenMissing_KeptWhenSet()
		{
			var client = Client(new DefaultHeadersMiddleware(new Dictionary<string, string> { ["X-App-Version"] = "1.2.3", ["X-Platform"] = "ios" }));

			Send(client, HttpRequest.Get("a").WithHeader("x-platform", "android"));

			var headers = _transport.Requests[0].Headers;
			Assert.AreEqual("1.2.3", headers["X-App-Version"]);
			Assert.AreEqual("android", headers["X-Platform"]);
		}

		[Test]
		public void DefaultContentType_DoesNotOverrideBodyContentType()
		{
			var client = Client(new DefaultHeadersMiddleware(new Dictionary<string, string> { ["content-type"] = "application/json" }));

			Send(client, HttpRequest.Post("upload").WithBody(new byte[] { 1, 2 }, "image/png"));
			Send(client, HttpRequest.Get("a"));

			Assert.IsFalse(_transport.Requests[0].Headers.ContainsKey("Content-Type"));
			Assert.AreEqual("image/png", _transport.Requests[0].ContentType);
			Assert.AreEqual("application/json", _transport.Requests[1].Headers["Content-Type"]);
		}

		[TestCase(ResultCodes.NetworkError)]
		[TestCase(ResultCodes.Timeout)]
		public void Retry_TransientFailure_RetriesUpToMaxAndReturnsLast(int code)
		{
			_transport.Respond = request => Result<HttpResponse>.Fail(code, "attempt " + _transport.Requests.Count);

			var result = Send(Client(Retry(maxRetries: 3)), HttpRequest.Get("a"));

			Assert.AreEqual(4, _transport.Requests.Count);
			Assert.AreEqual(code, result.Code);
			Assert.AreEqual("attempt 4", result.Message);
		}

		[Test]
		public void Retry_StopsAtFirstSuccess()
		{
			_transport.Respond = request => _transport.Requests.Count < 3
				? FakeTransport.Ok(request, 503)
				: FakeTransport.Ok(request, 200, "ok");

			var result = Send(Client(Retry()), HttpRequest.Get("a"));

			Assert.AreEqual(3, _transport.Requests.Count);
			Assert.AreEqual(200, result.Value.StatusCode);
		}

		[TestCase(408, true)]
		[TestCase(429, true)]
		[TestCase(500, true)]
		[TestCase(502, true)]
		[TestCase(503, true)]
		[TestCase(504, true)]
		[TestCase(400, false)]
		[TestCase(401, false)]
		[TestCase(404, false)]
		[TestCase(501, false)]
		public void Retry_StatusCodes(int statusCode, bool retried)
		{
			_transport.Respond = request => FakeTransport.Ok(request, statusCode);

			Send(Client(Retry(maxRetries: 1)), HttpRequest.Get("a"));

			Assert.AreEqual(retried ? 2 : 1, _transport.Requests.Count);
		}

		[TestCase(HttpMethod.Get, true)]
		[TestCase(HttpMethod.Head, true)]
		[TestCase(HttpMethod.Put, true)]
		[TestCase(HttpMethod.Delete, true)]
		[TestCase(HttpMethod.Post, false)]
		[TestCase(HttpMethod.Patch, false)]
		public void Retry_OnlyIdempotentMethods(HttpMethod method, bool retried)
		{
			_transport.Respond = request => Result<HttpResponse>.Fail(ResultCodes.NetworkError);

			Send(Client(Retry(maxRetries: 1)), new HttpRequest(method, "a"));

			Assert.AreEqual(retried ? 2 : 1, _transport.Requests.Count);
		}

		[Test]
		public void Retry_NeverRetriesCancelled()
		{
			_transport.Respond = request => Result<HttpResponse>.Fail(ResultCodes.Cancelled);

			Send(Client(Retry()), HttpRequest.Get("a"));

			Assert.AreEqual(1, _transport.Requests.Count);
		}

		[Test]
		public void Retry_DelaysGrowExponentiallyUpToMax()
		{
			_transport.Respond = request => Result<HttpResponse>.Fail(ResultCodes.NetworkError);

			Send(Client(Retry(maxRetries: 6, random: 1.0)), HttpRequest.Get("a"));

			CollectionAssert.AreEqual(new[] { 0.5, 1.0, 2.0, 4.0, 8.0, 8.0 }, _delays.ConvertAll(delay => delay.TotalSeconds));
		}

		[Test]
		public void Retry_JitterKeepsAtLeastHalfOfCap()
		{
			var retry = Retry(random: 0.0);

			Assert.AreEqual(TimeSpan.FromMilliseconds(250), retry.GetDelay(0));
			Assert.AreEqual(TimeSpan.FromSeconds(4), retry.GetDelay(10));
		}

		[Test]
		public void Retry_CancelledWhileWaiting_ReturnsCancelled()
		{
			_transport.Respond = request => Result<HttpResponse>.Fail(ResultCodes.NetworkError);

			using (var cancellation = new CancellationTokenSource())
			{
				var retry = new RetryMiddleware(3, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(8),
					(delay, cancellationToken) =>
					{
						cancellation.Cancel();
						cancellationToken.ThrowIfCancellationRequested();
						return UniTask.CompletedTask;
					},
					() => 1.0);

				var result = Send(Client(retry), HttpRequest.Get("a"), cancellation.Token);

				Assert.AreEqual(ResultCodes.Cancelled, result.Code);
				Assert.AreEqual(1, _transport.Requests.Count);
			}
		}

		[Test]
		public void Retry_ZeroRetries_SendsOnce()
		{
			_transport.Respond = request => Result<HttpResponse>.Fail(ResultCodes.NetworkError);

			Send(Client(Retry(maxRetries: 0)), HttpRequest.Get("a"));

			Assert.AreEqual(1, _transport.Requests.Count);
		}

		[Test]
		public void Retry_InvalidArguments_Throw()
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => new RetryMiddleware(-1));
			Assert.Throws<ArgumentOutOfRangeException>(() => new RetryMiddleware(3, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1)));
		}
	}
}
