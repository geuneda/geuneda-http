using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http.Tests
{
	[TestFixture]
	public class HttpClientTest
	{
		private sealed class RecordingMiddleware : IHttpMiddleware
		{
			private readonly string _name;
			private readonly List<string> _log;

			public RecordingMiddleware(string name, List<string> log)
			{
				_name = name;
				_log = log;
			}

			public async UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, HttpHandler next, CancellationToken cancellationToken)
			{
				_log.Add(_name + ">");
				var result = await next(request.WithHeader("X-Trace", (request.Headers.TryGetValue("X-Trace", out var trace) ? trace : "") + _name), cancellationToken);
				_log.Add("<" + _name);
				return result;
			}
		}

		private sealed class ThrowingCancelMiddleware : IHttpMiddleware
		{
			public UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, HttpHandler next, CancellationToken cancellationToken)
			{
				throw new OperationCanceledException();
			}
		}

		private sealed class Payload
		{
			public string Name;
			public int Count;
		}

		private FakeTransport _transport;

		[SetUp]
		public void SetUp()
		{
			_transport = new FakeTransport();
		}

		private HttpClientBuilder Builder()
		{
			return new HttpClientBuilder().WithBaseAddress("https://api.example.com/v1/").WithTransport(_transport);
		}

		[Test]
		public void Middlewares_RunByOrderThenRegistration_OuterFirst()
		{
			var log = new List<string>();
			var client = Builder()
				.AddMiddleware(new RecordingMiddleware("b", log), 10)
				.AddMiddleware(new RecordingMiddleware("a", log), 0)
				.AddMiddleware(new RecordingMiddleware("c", log), 10)
				.Build();

			FakeTransport.Run(client.GetAsync("users"));

			CollectionAssert.AreEqual(new[] { "a>", "b>", "c>", "<c", "<b", "<a" }, log);
			Assert.AreEqual("abc", _transport.Requests[0].Headers["x-trace"]);
		}

		[Test]
		public void RelativeUri_ResolvedAgainstBaseAddress()
		{
			var client = Builder().Build();

			FakeTransport.Run(client.GetAsync("users/7?expand=true"));
			FakeTransport.Run(client.GetAsync("https://other.example.com/ping"));

			Assert.AreEqual("https://api.example.com/v1/users/7?expand=true", _transport.Requests[0].Uri.AbsoluteUri);
			Assert.AreEqual("https://other.example.com/ping", _transport.Requests[1].Uri.AbsoluteUri);
		}

		[Test]
		public void RelativeUri_WithoutBaseAddress_Throws()
		{
			var client = new HttpClientBuilder().WithTransport(_transport).Build();

			Assert.Throws<ArgumentException>(() => client.SendAsync(HttpRequest.Get("users")).GetAwaiter().GetResult());
		}

		[Test]
		public void MiddlewareThrowsCancellation_WhenCallerCancelled_ReturnsCancelled()
		{
			var client = Builder().AddMiddleware(new ThrowingCancelMiddleware()).Build();

			using (var cancellation = new CancellationTokenSource())
			{
				cancellation.Cancel();

				var result = FakeTransport.Run(client.GetAsync("users", cancellation.Token));

				Assert.AreEqual(ResultCodes.Cancelled, result.Code);
			}
		}

		[Test]
		public void MiddlewareThrowsCancellation_WithoutCallerCancel_Propagates()
		{
			var client = Builder().AddMiddleware(new ThrowingCancelMiddleware()).Build();

			Assert.Throws<OperationCanceledException>(() => client.GetAsync("users").GetAwaiter().GetResult());
		}

		[Test]
		public void HttpErrorStatus_IsSuccessfulResultWithStatusCode()
		{
			_transport.Respond = request => FakeTransport.Ok(request, 404, "not here");

			var result = FakeTransport.Run(Builder().Build().GetAsync("users/9"));

			Assert.IsTrue(result.IsSuccess);
			Assert.AreEqual(404, result.Value.StatusCode);
			Assert.IsFalse(result.Value.IsSuccessStatusCode);
			Assert.AreEqual("not here", result.Value.ReadText());
		}

		[Test]
		public void GetJson_ReadsSuccessBody()
		{
			_transport.Respond = request => FakeTransport.Ok(request, 200, "{\"Name\":\"geuneda\",\"Count\":3}");

			var result = FakeTransport.Run(Builder().Build().GetJsonAsync<Payload>("payload"));

			Assert.AreEqual("geuneda", result.Value.Name);
			Assert.AreEqual(3, result.Value.Count);
		}

		[Test]
		public void GetJson_NonSuccessStatus_IsHttpError()
		{
			_transport.Respond = request => FakeTransport.Ok(request, 503, "{}");

			var result = FakeTransport.Run(Builder().Build().GetJsonAsync<Payload>("payload"));

			Assert.AreEqual(HttpResultCodes.HttpError, result.Code);
			StringAssert.Contains("503", result.Message);
		}

		[TestCase("not json")]
		[TestCase("")]
		[TestCase(" \r\n\t")]
		public void GetJson_UnreadableBody_IsInvalidResponse(string body)
		{
			_transport.Respond = request => FakeTransport.Ok(request, 200, body);

			var result = FakeTransport.Run(Builder().Build().GetJsonAsync<Payload>("payload"));

			Assert.AreEqual(HttpResultCodes.InvalidResponse, result.Code);
		}

		[TestCase("null")]
		[TestCase(" null \n")]
		public void GetJson_JsonNullLiteral_ReferenceAndNullableTypesSucceedWithNull(string body)
		{
			_transport.Respond = request => FakeTransport.Ok(request, 200, body);
			var client = Builder().Build();

			var reference = FakeTransport.Run(client.GetJsonAsync<Payload>("payload"));
			var nullable = FakeTransport.Run(client.GetJsonAsync<int?>("count"));

			Assert.IsTrue(reference.IsSuccess);
			Assert.IsNull(reference.Value);
			Assert.IsTrue(nullable.IsSuccess);
			Assert.IsNull(nullable.Value);
		}

		[Test]
		public void GetJson_JsonNullLiteral_NonNullableValueTypeIsInvalidResponse()
		{
			_transport.Respond = request => FakeTransport.Ok(request, 200, "null");

			var result = FakeTransport.Run(Builder().Build().GetJsonAsync<int>("count"));

			Assert.AreEqual(HttpResultCodes.InvalidResponse, result.Code);
		}

		[Test]
		public void GetJson_TransportFailure_KeepsCode()
		{
			_transport.Respond = request => Result<HttpResponse>.Fail(ResultCodes.NetworkError, "offline");

			var result = FakeTransport.Run(Builder().Build().GetJsonAsync<Payload>("payload"));

			Assert.AreEqual(ResultCodes.NetworkError, result.Code);
			Assert.AreEqual("offline", result.Message);
		}

		[Test]
		public void PostJson_SendsJsonBody()
		{
			_transport.Respond = request => FakeTransport.Ok(request, 201, "{\"Name\":\"created\",\"Count\":1}");

			var result = FakeTransport.Run(Builder().Build().PostJsonAsync<Payload>("items", new Payload { Name = "new", Count = 2 }));

			var sent = _transport.Requests[0];
			Assert.AreEqual(HttpMethod.Post, sent.Method);
			Assert.AreEqual("application/json", sent.ContentType);
			Assert.AreEqual("{\"Name\":\"new\",\"Count\":2}", Encoding.UTF8.GetString(sent.Body));
			Assert.AreEqual("created", result.Value.Name);
		}

		[Test]
		public void Request_WithMethodsReturnCopies()
		{
			var original = HttpRequest.Get("users");

			var changed = original.WithHeader("X-Key", "1").WithTimeout(TimeSpan.FromSeconds(3));

			CollectionAssert.IsEmpty(original.Headers);
			Assert.IsNull(original.Timeout);
			Assert.AreEqual("1", changed.Headers["x-key"]);
			Assert.AreEqual(TimeSpan.FromSeconds(3), changed.Timeout);
			Assert.AreEqual("2", changed.WithHeader("x-KEY", "2").Headers["X-Key"]);
		}

		[Test]
		public void Request_InvalidArguments_Throw()
		{
			Assert.Throws<ArgumentException>(() => HttpRequest.Get(""));
			Assert.Throws<ArgumentException>(() => HttpRequest.Get((string)null));
			Assert.Throws<ArgumentNullException>(() => new HttpRequest(HttpMethod.Get, (Uri)null));
			Assert.Throws<ArgumentOutOfRangeException>(() => new HttpRequest((HttpMethod)999, "users"));
			Assert.Throws<ArgumentOutOfRangeException>(() => new HttpRequest((HttpMethod)(-1), "users"));
			Assert.Throws<ArgumentOutOfRangeException>(() => HttpRequest.Get("users").WithTimeout(TimeSpan.Zero));
			Assert.Throws<ArgumentException>(() => HttpRequest.Get("users").WithHeader("", "v"));
			Assert.Throws<ArgumentNullException>(() => HttpRequest.Get("users").WithHeader("X", null));
		}

		[Test]
		public void Headers_CannotBeChangedFromOutside()
		{
			var empty = HttpRequest.Get("a");
			var withHeader = empty.WithHeader("X-Key", "1");
			var response = new HttpResponse(new Uri("https://example.com/"), 200, null, new Dictionary<string, string> { ["X"] = "1" });

			Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)empty.Headers)["X-Leak"] = "v");
			Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)withHeader.Headers)["X-Key"] = "2");
			Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)response.Headers)["X"] = "2");
			CollectionAssert.IsEmpty(HttpRequest.Get("b").Headers);
			Assert.AreEqual("1", withHeader.Headers["X-Key"]);
		}

		[Test]
		public void Builder_InvalidArguments_Throw()
		{
			Assert.Throws<ArgumentException>(() => new HttpClientBuilder().WithBaseAddress("relative/path"));
			Assert.Throws<ArgumentNullException>(() => new HttpClientBuilder().AddMiddleware(null));
			Assert.Throws<ArgumentNullException>(() => new HttpClientBuilder().WithTransport(null));
		}

		[TestCase(199, false)]
		[TestCase(200, true)]
		[TestCase(299, true)]
		[TestCase(300, false)]
		public void Response_IsSuccessStatusCode(int statusCode, bool expected)
		{
			var response = new HttpResponse(new Uri("https://example.com/"), statusCode, null,
				new Dictionary<string, string> { ["Content-Type"] = "text/plain" });

			Assert.AreEqual(expected, response.IsSuccessStatusCode);
			Assert.AreEqual("text/plain", response.Headers["content-type"]);
			Assert.AreEqual(0, response.Body.Length);
		}
	}
}
