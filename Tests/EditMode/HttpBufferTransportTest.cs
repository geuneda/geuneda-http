using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http.Tests
{
	[TestFixture]
	public class HttpBufferTransportTest
	{
		// Java NativeHttpChannelTest.GOLDEN_FRAME 과 같은 바이트
		private const string GoldenFrame =
			"0000000100000004504f5354000005dc0000000d68747470733a2f2f612e622f63000000106170706c69636174696f6e2f6a736f6e"
			+ "0000000100000003582d410000000131000000027b7d";

		private sealed class FakeChannel : INativeHttpChannel
		{
			public int State = NativeHttpState.Done;
			public int Status = 200;
			public byte[] Response = Array.Empty<byte>();
			public byte[] LastFrame;
			public bool ThrowOnStart;
			public int Starts;
			public int Cancels;
			public bool Disposed;

			public int StatusCode => Status;

			public int ResponseLength => State == NativeHttpState.Done ? Response.Length : 0;

			public string Error => "boom";

			public void Start(byte[] frame, int length)
			{
				if (ThrowOnStart)
				{
					throw new InvalidOperationException("start");
				}

				if (LastFrame == null || LastFrame.Length != length)
				{
					LastFrame = new byte[length];
				}

				Buffer.BlockCopy(frame, 0, LastFrame, 0, length);
				Starts++;
			}

			public int Poll() => State;

			public void CopyResponse(byte[] destination, int length) => Buffer.BlockCopy(Response, 0, destination, 0, length);

			public void Cancel()
			{
				Cancels++;
				State = NativeHttpState.Idle;
			}

			public void Dispose() => Disposed = true;
		}

		private List<FakeChannel> _channels;
		private NativeHttpTransport _transport;

		[SetUp]
		public void SetUp()
		{
			_channels = new List<FakeChannel>();
			_transport = new NativeHttpTransport(() =>
			{
				var channel = new FakeChannel();
				_channels.Add(channel);
				return channel;
			});
		}

		[TearDown]
		public void TearDown() => _transport.Dispose();

		private static HttpBufferRequest GoldenRequest()
		{
			var request = new HttpBufferRequest { Url = "https://a.b/c", Timeout = TimeSpan.FromMilliseconds(1500) };
			request.SetHeader("X-A", "1");
			request.SetHeader("content-type", "text/plain");
			request.SetBody(Encoding.UTF8.GetBytes("{}"), "application/json");
			return request;
		}

		private static string Hex(byte[] bytes, int length)
		{
			var builder = new StringBuilder(length * 2);

			for (var i = 0; i < length; i++)
			{
				builder.Append(bytes[i].ToString("x2"));
			}

			return builder.ToString();
		}

		private static T Run<T>(UniTask<T> task) => FakeTransport.Run(task);

		// Debug 컴파일(에디터 기본)에서는 async 상태 기계가 클래스라 호출마다 할당한다. 플레이어(Release)와 같은 조건에서만 잰다
		private static void RequireReleaseCode()
		{
			if (CompilationPipeline.codeOptimization != CodeOptimization.Release)
			{
				Assert.Ignore("Allocation tests need Release code optimization (-releaseCodeOptimization)");
			}
		}

		[Test]
		public void Frame_MatchesJavaGoldenBytes_AndSkipsHeaderContentTypeWithBody()
		{
			byte[] buffer = null;
			var length = HttpBufferFrame.Write(GoldenRequest(), ref buffer);

			Assert.AreEqual(GoldenFrame, Hex(buffer, length));
		}

		[Test]
		public void Frame_WithoutBody_KeepsContentTypeHeader_AndWritesMinusOne()
		{
			var request = new HttpBufferRequest { Method = HttpMethod.Get, Url = "http://h/" };
			request.SetHeader("Content-Type", "x");
			byte[] buffer = new byte[1];
			var length = HttpBufferFrame.Write(request, ref buffer);

			StringAssert.Contains(Hex(Encoding.UTF8.GetBytes("Content-Type"), 12), Hex(buffer, length));
			StringAssert.EndsWith("ffffffff", Hex(buffer, length));
			// 제한 시간 없음은 0
			StringAssert.StartsWith("0000000100000003474554" + "00000000", Hex(buffer, length));
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("/relative")]
		[TestCase("ftp://h/")]
		[TestCase("https://")]
		public void Frame_NonHttpAbsoluteUrl_Throws(string url)
		{
			byte[] buffer = null;
			Assert.Throws<ArgumentException>(() => HttpBufferFrame.Write(new HttpBufferRequest { Url = url }, ref buffer));
		}

		[Test]
		public void Request_SetHeader_ReplacesIgnoringCase_RemoveAndClear()
		{
			var request = new HttpBufferRequest();

			for (var i = 0; i < 20; i++)
			{
				request.SetHeader("H" + i, "v");
			}

			request.SetHeader("h3", "new");
			Assert.AreEqual(20, request.HeaderCount);
			Assert.AreEqual("H3", request.GetHeaderName(3));
			Assert.AreEqual("new", request.GetHeaderValue(3));

			request.RemoveHeader("H0");
			Assert.AreEqual(19, request.HeaderCount);
			Assert.AreEqual("H19", request.GetHeaderName(0));

			request.ClearHeaders();
			Assert.AreEqual(0, request.HeaderCount);
			Assert.Throws<ArgumentOutOfRangeException>(() => request.GetHeaderName(0));
		}

		[Test]
		public void Request_SetBody_Copies_AndClearBodyRemovesIt()
		{
			var request = new HttpBufferRequest();
			var source = new byte[] { 1, 2, 3 };
			request.SetBody(source, "a/b");
			source[0] = 9;

			Assert.AreEqual(new byte[] { 1, 2, 3 }, request.Body.ToArray());
			request.ClearBody();
			Assert.IsFalse(request.HasBody);
			Assert.IsNull(request.ContentType);
		}

		[Test]
		public void Send_Done_CopiesStatusAndBody()
		{
			var request = GoldenRequest();
			var response = new HttpBufferResponse(1);
			_channels[0].Status = 404;
			_channels[0].Response = Encoding.UTF8.GetBytes("not here");

			var result = Run(_transport.SendAsync(request, response, CancellationToken.None));

			Assert.IsTrue(result.IsSuccess);
			Assert.AreEqual(404, response.StatusCode);
			Assert.IsFalse(response.IsSuccessStatusCode);
			Assert.AreEqual("not here", response.ReadText());
			Assert.AreEqual(GoldenFrame, Hex(_channels[0].LastFrame, _channels[0].LastFrame.Length));
		}

		// 반복 요청의 본문이 기본 프레임(16KB)보다 커도 Reserve 해 두면 요청 도중 프레임을 새로 만들지 않는다
		[Test]
		public void Reserve_ThenLargerRequest_KeepsFrame()
		{
			_transport.Reserve(64 * 1024);
			Assert.AreEqual(64 * 1024, _transport.FrameCapacity);
			var request = GoldenRequest();
			request.SetBody(new byte[40 * 1024], "application/json");

			Assert.IsTrue(Run(_transport.SendAsync(request, new HttpBufferResponse(), CancellationToken.None)).IsSuccess);
			Assert.AreEqual(64 * 1024, _transport.FrameCapacity, "the reserved frame is reused");
			_transport.Reserve(1024);
			Assert.AreEqual(64 * 1024, _transport.FrameCapacity, "reserve never shrinks");
		}

		// 프레임을 키울 때는 반만큼 여유를 둬 바로 다음 조금 큰 요청에서 다시 키우지 않는다
		[Test]
		public void FrameGrowth_LeavesHeadroom()
		{
			var request = GoldenRequest();
			request.SetBody(new byte[40 * 1024], "application/json");
			Run(_transport.SendAsync(request, new HttpBufferResponse(), CancellationToken.None));
			Assert.GreaterOrEqual(_transport.FrameCapacity, 60 * 1024);
		}

		[TestCase(NativeHttpState.NetworkError, ResultCodes.NetworkError)]
		[TestCase(NativeHttpState.Timeout, ResultCodes.Timeout)]
		[TestCase(NativeHttpState.Cancelled, ResultCodes.Cancelled)]
		public void Send_NativeFailure_MapsToResultCode(int state, int code)
		{
			var response = new HttpBufferResponse();
			_channels[0].State = state;

			var result = Run(_transport.SendAsync(GoldenRequest(), response, CancellationToken.None));

			Assert.AreEqual(code, result.Code);
			Assert.AreEqual(0, response.StatusCode);
			Assert.AreEqual(1, _channels.Count, "the channel is reused");
		}

		[Test]
		public void Send_AlreadyCancelled_DoesNotStart()
		{
			var result = Run(_transport.SendAsync(GoldenRequest(), new HttpBufferResponse(), new CancellationToken(true)));

			Assert.AreEqual(ResultCodes.Cancelled, result.Code);
			Assert.AreEqual(0, _channels[0].Starts);
		}

		[Test]
		public void Send_UnsupportedMethods_Throw()
		{
			var patch = new HttpBufferRequest { Method = HttpMethod.Patch, Url = "https://h/" };
			var getWithBody = new HttpBufferRequest { Method = HttpMethod.Get, Url = "https://h/" };
			getWithBody.SetBody(new byte[1], "a/b");

			Assert.Throws<NotSupportedException>(() => Run(_transport.SendAsync(patch, new HttpBufferResponse(), CancellationToken.None)));
			Assert.Throws<NotSupportedException>(() => Run(_transport.SendAsync(getWithBody, new HttpBufferResponse(), CancellationToken.None)));
		}

		[Test]
		public void Send_StartThrows_DisposesChannel_AndNextSendUsesNewOne()
		{
			_channels[0].ThrowOnStart = true;

			Assert.Throws<InvalidOperationException>(() => Run(_transport.SendAsync(GoldenRequest(), new HttpBufferResponse(), CancellationToken.None)));
			Assert.IsTrue(_channels[0].Disposed);

			var result = Run(_transport.SendAsync(GoldenRequest(), new HttpBufferResponse(), CancellationToken.None));
			Assert.IsTrue(result.IsSuccess);
			Assert.AreEqual(2, _channels.Count);
		}

		[UnityTest]
		public IEnumerator Send_Running_ThenDone_CompletesOnLaterFrame() => UniTask.ToCoroutine(async () =>
		{
			_channels[0].State = NativeHttpState.Running;
			_channels[0].Response = new byte[] { 7 };
			var response = new HttpBufferResponse();
			var task = _transport.SendAsync(GoldenRequest(), response, CancellationToken.None);

			await UniTask.Yield();
			Assert.IsFalse(task.Status.IsCompleted());
			_channels[0].State = NativeHttpState.Done;
			var result = await task;

			Assert.IsTrue(result.IsSuccess);
			Assert.AreEqual(1, response.Length);
		});

		[UnityTest]
		public IEnumerator Send_Running_CallerCancels_CancelsNative_AndReusesChannel() => UniTask.ToCoroutine(async () =>
		{
			_channels[0].State = NativeHttpState.Running;
			var cancel = new CancellationTokenSource();
			var task = _transport.SendAsync(GoldenRequest(), new HttpBufferResponse(), cancel.Token);

			await UniTask.Yield();
			cancel.Cancel();
			var result = await task;

			Assert.AreEqual(ResultCodes.Cancelled, result.Code);
			Assert.AreEqual(1, _channels[0].Cancels);
			_channels[0].State = NativeHttpState.Done;
			Assert.IsTrue((await _transport.SendAsync(GoldenRequest(), new HttpBufferResponse(), CancellationToken.None)).IsSuccess);
			Assert.AreEqual(1, _channels.Count);
		});

		[UnityTest]
		public IEnumerator Send_RunningPastTimeout_IsTimeout_AndCancelsNative() => UniTask.ToCoroutine(async () =>
		{
			_channels[0].State = NativeHttpState.Running;
			var request = GoldenRequest();
			request.Timeout = TimeSpan.FromMilliseconds(30);

			var result = await _transport.SendAsync(request, new HttpBufferResponse(), CancellationToken.None);

			Assert.AreEqual(ResultCodes.Timeout, result.Code);
			Assert.AreEqual(1, _channels[0].Cancels);
		});

		[UnityTest]
		public IEnumerator Send_Concurrent_UsesSecondChannel() => UniTask.ToCoroutine(async () =>
		{
			_channels[0].State = NativeHttpState.Running;
			var first = _transport.SendAsync(GoldenRequest(), new HttpBufferResponse(), CancellationToken.None);
			var second = Run(_transport.SendAsync(GoldenRequest(), new HttpBufferResponse(), CancellationToken.None));

			Assert.IsTrue(second.IsSuccess);
			Assert.AreEqual(2, _channels.Count);
			_channels[0].State = NativeHttpState.Done;
			Assert.IsTrue((await first).IsSuccess);
		});

		[UnityTest]
		public IEnumerator Dispose_WhileRunning_EndsCancelled_AndDisposesChannel() => UniTask.ToCoroutine(async () =>
		{
			_channels[0].State = NativeHttpState.Running;
			var task = _transport.SendAsync(GoldenRequest(), new HttpBufferResponse(), CancellationToken.None);

			_transport.Dispose();
			var result = await task;

			Assert.AreEqual(ResultCodes.Cancelled, result.Code);
			Assert.IsTrue(_channels[0].Disposed);
			Assert.Throws<ObjectDisposedException>(() => Run(_transport.SendAsync(GoldenRequest(), new HttpBufferResponse(), CancellationToken.None)));
		});

		[UnityTest]
		public IEnumerator Send_CancelledWhileWaiting_ThenDone_IsCancelled() => UniTask.ToCoroutine(async () =>
		{
			_channels[0].State = NativeHttpState.Running;
			var cancel = new CancellationTokenSource();
			var response = new HttpBufferResponse();
			var task = _transport.SendAsync(GoldenRequest(), response, cancel.Token);

			cancel.Cancel();
			_channels[0].State = NativeHttpState.Done;
			var result = await task;

			Assert.AreEqual(ResultCodes.Cancelled, result.Code);
			Assert.AreEqual(0, response.StatusCode);
		});

		[UnityTest]
		public IEnumerator Send_DisposedWhileWaiting_ThenDone_IsCancelled() => UniTask.ToCoroutine(async () =>
		{
			_channels[0].State = NativeHttpState.Running;
			var task = _transport.SendAsync(GoldenRequest(), new HttpBufferResponse(), CancellationToken.None);

			((IHttpBufferTransport)_transport).Dispose();
			_channels[0].State = NativeHttpState.Done;

			Assert.AreEqual(ResultCodes.Cancelled, (await task).Code);
			Assert.IsTrue(_channels[0].Disposed);
		});

		[UnityTest]
		public IEnumerator Send_TimedOutWhileWaiting_ThenDone_IsTimeout() => UniTask.ToCoroutine(async () =>
		{
			_channels[0].State = NativeHttpState.Running;
			var request = GoldenRequest();
			request.Timeout = TimeSpan.FromMilliseconds(20);
			var task = _transport.SendAsync(request, new HttpBufferResponse(), CancellationToken.None);

			// 전송이 다시 돌기 전에 제한 시간을 넘기고 끝난 상태로 만든다
			Thread.Sleep(60);
			_channels[0].State = NativeHttpState.Done;

			Assert.AreEqual(ResultCodes.Timeout, (await task).Code);
		});

		[Test]
		public void Send_InvalidRequest_ThrowsArgument_AndDropsChannel()
		{
			_channels[0].State = NativeHttpState.InvalidRequest;

			Assert.Throws<ArgumentException>(() => Run(_transport.SendAsync(GoldenRequest(), new HttpBufferResponse(), CancellationToken.None)));
			Assert.IsTrue(_channels[0].Disposed);
		}

		[Test]
		public void BufferFallback_MalformedUrl_ThrowsArgument()
		{
			var transport = new UnityWebRequestBufferTransport(new FakeTransport());

			Assert.Throws<ArgumentException>(() => Run(transport.SendAsync(new HttpBufferRequest { Url = "https://h:bad/" }, new HttpBufferResponse(), CancellationToken.None)));
		}

		[Test]
		public void Send_Warm_DoesNotAllocate()
		{
			RequireReleaseCode();
			var request = GoldenRequest();
			var response = new HttpBufferResponse();
			_channels[0].Response = new byte[300];
			TestDelegate send = () =>
			{
				// 요청마다 바뀌는 헤더(토큰)를 다시 넣는 것까지 잰다
				request.SetHeader("Authorization", "Bearer abc");
				request.SetBody(_channels[0].Response, "application/json");
				_transport.SendAsync(request, response, CancellationToken.None).GetAwaiter().GetResult();
			};
			send();

			Assert.That(send, Is.Not.AllocatingGCMemory());
		}

		[UnityTest]
		public IEnumerator Send_WarmWaitingStart_DoesNotAllocate()
		{
			RequireReleaseCode();
			return WarmWaitingStart();
		}

		private IEnumerator WarmWaitingStart() => UniTask.ToCoroutine(async () =>
		{
			var request = GoldenRequest();
			var response = new HttpBufferResponse();
			var task = default(UniTask<Result>);
			TestDelegate start = () => task = _transport.SendAsync(request, response, CancellationToken.None);

			for (var round = 0; round < 2; round++)
			{
				_channels[0].State = NativeHttpState.Running;

				if (round == 0)
				{
					start();
				}
				else
				{
					// 두 번째부터는 상태 기계·대기가 풀에서 나온다
					Assert.That(start, Is.Not.AllocatingGCMemory());
				}

				await UniTask.Yield();
				_channels[0].State = NativeHttpState.Done;
				Assert.IsTrue((await task).IsSuccess);
			}
		});

		[Test]
		public void BufferFallback_ConvertsRequest_AndCopiesResponse()
		{
			var inner = new FakeTransport { Respond = r => FakeTransport.Ok(r, 202, "ok") };
			var transport = new UnityWebRequestBufferTransport(inner);
			var response = new HttpBufferResponse(0);

			var result = Run(transport.SendAsync(GoldenRequest(), response, CancellationToken.None));

			Assert.IsTrue(result.IsSuccess);
			Assert.AreEqual(202, response.StatusCode);
			Assert.AreEqual("ok", response.ReadText());
			var sent = inner.Requests[0];
			Assert.AreEqual(HttpMethod.Post, sent.Method);
			Assert.AreEqual("https://a.b/c", sent.Uri.ToString());
			Assert.AreEqual("1", sent.Headers["X-A"]);
			Assert.AreEqual("application/json", sent.ContentType);
			Assert.AreEqual("{}", Encoding.UTF8.GetString(sent.Body));
			Assert.AreEqual(TimeSpan.FromMilliseconds(1500), sent.Timeout);
		}

		[Test]
		public void BufferFallback_Failure_KeepsCode()
		{
			var inner = new FakeTransport { Respond = r => Result<HttpResponse>.Fail(ResultCodes.Timeout) };
			var response = new HttpBufferResponse();

			var result = Run(new UnityWebRequestBufferTransport(inner).SendAsync(GoldenRequest(), response, CancellationToken.None));

			Assert.AreEqual(ResultCodes.Timeout, result.Code);
			Assert.AreEqual(0, response.StatusCode);
		}
	}
}
