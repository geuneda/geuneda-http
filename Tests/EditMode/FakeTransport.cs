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
	/// <summary>
	/// 받은 요청을 기록하고 정해진 결과를 바로 돌려주는 전송입니다. 테스트의 비동기 흐름이 모두 동기적으로 끝납니다.
	/// </summary>
	internal sealed class FakeTransport : IHttpTransport
	{
		public readonly List<HttpRequest> Requests = new List<HttpRequest>();
		public Func<HttpRequest, Result<HttpResponse>> Respond = request => Ok(request, 200);

		public UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, CancellationToken cancellationToken)
		{
			Requests.Add(request);
			return UniTask.FromResult(Respond(request));
		}

		public static Result<HttpResponse> Ok(HttpRequest request, int statusCode, string body = "")
		{
			return Result<HttpResponse>.Ok(new HttpResponse(request.Uri, statusCode, Encoding.UTF8.GetBytes(body), null));
		}

		/// <summary>이미 끝난 UniTask 의 결과를 꺼냅니다. 끝나지 않았으면 테스트 실패입니다.</summary>
		public static T Run<T>(UniTask<T> task)
		{
			Assert.IsTrue(task.Status.IsCompleted(), "Expected the task to complete synchronously");
			return task.GetAwaiter().GetResult();
		}
	}
}
