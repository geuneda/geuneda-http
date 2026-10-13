using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	public enum HttpMethod
	{
		Get,
		Head,
		Post,
		Put,
		Patch,
		Delete
	}

	internal static class HttpMethodExtensions
	{
		public static string ToVerb(this HttpMethod method)
		{
			switch (method)
			{
				case HttpMethod.Head:
					return "HEAD";
				case HttpMethod.Post:
					return "POST";
				case HttpMethod.Put:
					return "PUT";
				case HttpMethod.Patch:
					return "PATCH";
				case HttpMethod.Delete:
					return "DELETE";
				case HttpMethod.Get:
					return "GET";
				default:
					throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown HTTP method");
			}
		}

		/// <summary>
		/// 같은 요청을 여러 번 보내도 결과가 같은 메서드(GET, HEAD, PUT, DELETE)입니다. 재시도해도 되는지 판단할 때 씁니다.
		/// </summary>
		public static bool IsIdempotent(this HttpMethod method)
		{
			return method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Put || method == HttpMethod.Delete;
		}
	}
}
