using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using NUnit.Framework;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http.Tests.PlayMode
{
	/// <summary>
	/// 로컬 <see cref="HttpListener"/> 서버로 실제 <see cref="UnityWebRequestTransport"/> 동작을 확인합니다.
	/// </summary>
	public class UnityWebRequestTransportTest
	{
		private sealed class TestServer : IDisposable
		{
			private readonly HttpListener _listener = new HttpListener();
			private readonly CancellationTokenSource _stop = new CancellationTokenSource();
			private int _countedRequests;

			public int CountedRequests => Volatile.Read(ref _countedRequests);

			public string BaseAddress { get; }

			public TestServer()
			{
				BaseAddress = $"http://127.0.0.1:{GetFreePort()}/";
				_listener.Prefixes.Add(BaseAddress);
				_listener.Start();
				Task.Run(AcceptLoop);
			}

			public void Dispose()
			{
				_stop.Cancel();
				_listener.Close();
			}

			private async Task AcceptLoop()
			{
				while (!_stop.IsCancellationRequested)
				{
					HttpListenerContext context;

					try
					{
						context = await _listener.GetContextAsync();
					}
					catch (Exception)
					{
						return;
					}

					_ = Task.Run(() => Handle(context));
				}
			}

			private async Task Handle(HttpListenerContext context)
			{
				var response = context.Response;

				try
				{
					switch (context.Request.Url.AbsolutePath)
					{
						case "/json":
							response.AddHeader("X-Test", "yes");
							await Write(response, 200, "{\"Name\":\"geuneda\",\"Count\":3}");
							break;
						case "/echo":
							string body;
							using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
							{
								body = await reader.ReadToEndAsync();
							}

							response.AddHeader("X-Echo-Content-Type", context.Request.ContentType ?? "");
							response.AddHeader("X-Echo-Method", context.Request.HttpMethod);
							await Write(response, 200, body);
							break;
						case "/count":
							Interlocked.Increment(ref _countedRequests);
							await Write(response, 200, "counted");
							break;
						case "/slow":
							await Task.Delay(3000, _stop.Token);
							await Write(response, 200, "late");
							break;
						default:
							await Write(response, 404, "not here");
							break;
					}
				}
				catch (Exception)
				{
					// 클라이언트가 끊거나 서버를 닫으면 무시한다
				}
			}

			private static async Task Write(HttpListenerResponse response, int statusCode, string body)
			{
				var bytes = Encoding.UTF8.GetBytes(body);
				response.StatusCode = statusCode;
				response.ContentLength64 = bytes.Length;
				await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
				response.Close();
			}
		}

		private sealed class Payload
		{
			public string Name;
			public int Count;
		}

		private TestServer _server;
		private HttpClient _client;

		[SetUp]
		public void SetUp()
		{
			_server = new TestServer();
			_client = new HttpClientBuilder().WithBaseAddress(_server.BaseAddress).Build();
		}

		[TearDown]
		public void TearDown()
		{
			_server.Dispose();
		}

		private static int GetFreePort()
		{
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			var port = ((IPEndPoint)listener.LocalEndpoint).Port;
			listener.Stop();
			return port;
		}

		[UnityTest]
		public IEnumerator GetJson_ReadsBodyAndHeaders() => UniTask.ToCoroutine(async () =>
		{
			var json = await _client.GetJsonAsync<Payload>("json");
			var raw = await _client.GetAsync("json");

			Assert.AreEqual("geuneda", json.Value.Name);
			Assert.AreEqual(3, json.Value.Count);
			Assert.AreEqual(200, raw.Value.StatusCode);
			Assert.AreEqual("yes", raw.Value.Headers["x-test"]);
		});

		[UnityTest]
		public IEnumerator NotFound_IsResponseWithStatusAndBody() => UniTask.ToCoroutine(async () =>
		{
			var result = await _client.GetAsync("missing");

			Assert.IsTrue(result.IsSuccess);
			Assert.AreEqual(404, result.Value.StatusCode);
			Assert.AreEqual("not here", result.Value.ReadText());
		});

		[UnityTest]
		public IEnumerator PostJson_SendsBodyAndContentType() => UniTask.ToCoroutine(async () =>
		{
			var result = await _client.PostJsonAsync("echo", new Payload { Name = "sent", Count = 5 });

			Assert.AreEqual("{\"Name\":\"sent\",\"Count\":5}", result.Value.ReadText());
			StringAssert.StartsWith("application/json", result.Value.Headers["X-Echo-Content-Type"]);
			Assert.AreEqual("POST", result.Value.Headers["X-Echo-Method"]);
		});

		[UnityTest]
		public IEnumerator ConnectionRefused_IsNetworkError() => UniTask.ToCoroutine(async () =>
		{
			var result = await _client.GetAsync($"http://127.0.0.1:{GetFreePort()}/nothing");

			Assert.AreEqual(ResultCodes.NetworkError, result.Code);
		});

		[UnityTest]
		public IEnumerator SlowResponse_PastTimeout_IsTimeout() => UniTask.ToCoroutine(async () =>
		{
			var result = await _client.SendAsync(HttpRequest.Get("slow").WithTimeout(TimeSpan.FromMilliseconds(300)));

			Assert.AreEqual(ResultCodes.Timeout, result.Code);
		});

		[UnityTest]
		public IEnumerator CallerCancel_IsCancelled() => UniTask.ToCoroutine(async () =>
		{
			using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
			{
				var result = await _client.GetAsync("slow", cancellation.Token);

				Assert.AreEqual(ResultCodes.Cancelled, result.Code);
			}
		});

		[UnityTest]
		public IEnumerator BodyContentType_WinsOverHeaderAndDefaults() => UniTask.ToCoroutine(async () =>
		{
			var client = new HttpClientBuilder()
				.WithBaseAddress(_server.BaseAddress)
				.AddMiddleware(new DefaultHeadersMiddleware(new System.Collections.Generic.Dictionary<string, string> { ["Content-Type"] = "application/json" }))
				.Build();
			var request = HttpRequest.Post("echo").WithHeader("Content-Type", "text/plain").WithBody(new byte[] { 1, 2, 3 }, "image/png");

			var result = await client.SendAsync(request);

			Assert.AreEqual("image/png", result.Value.Headers["X-Echo-Content-Type"]);
		});

		[UnityTest]
		public IEnumerator CancelledWhileSwitchingToMainThread_DoesNotSend() => UniTask.ToCoroutine(async () =>
		{
			var transport = new UnityWebRequestTransport();
			var request = HttpRequest.Post(_server.BaseAddress + "count").WithJsonBody(new Payload { Name = "x" });

			using (var cancellation = new CancellationTokenSource())
			using (var startedAndCancelled = new ManualResetEventSlim(false))
			{
				var pending = default(UniTask<Result<HttpResponse>>);

				// 작업 스레드에서 보내기 시작한 뒤(메인 스레드 전환 대기) 바로 취소한다
				ThreadPool.QueueUserWorkItem(_ =>
				{
					pending = transport.SendAsync(request, cancellation.Token);
					cancellation.Cancel();
					startedAndCancelled.Set();
				});

				// 메인 스레드가 여기서 막혀 있는 동안에는 전환 continuation 이 실행될 수 없다. 그래서 재개 전에 취소가 끝남이 보장된다
				Assert.IsTrue(startedAndCancelled.Wait(TimeSpan.FromSeconds(5)), "worker did not start the request");

				var result = await pending;

				Assert.AreEqual(ResultCodes.Cancelled, result.Code);
			}

			await UniTask.Delay(500, true);
			Assert.AreEqual(0, _server.CountedRequests);
		});

		[UnityTest]
		public IEnumerator SendFromBackgroundThread_Works() => UniTask.ToCoroutine(async () =>
		{
			var result = await UniTask.RunOnThreadPool(() => _client.GetJsonAsync<Payload>("json"));

			Assert.AreEqual("geuneda", result.Value.Name);
		});
	}
}
