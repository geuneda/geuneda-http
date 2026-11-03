#if UNITY_ANDROID
using System;
using Geuneda.Core;
using UnityEngine;
#endif

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>플랫폼에 맞는 <see cref="IHttpBufferTransport"/> 를 고릅니다.</summary>
	public static class HttpBufferTransports
	{
#if UNITY_ANDROID
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.Http");
#endif

		/// <summary>
		/// 안드로이드 플레이어는 <see cref="NativeHttpTransport"/>, 그 밖(에디터·iOS 등)과 Java 플러그인을 못 찾을 때는 <see cref="UnityWebRequestBufferTransport"/> 입니다.
		/// 메인 스레드에서 부릅니다.
		/// </summary>
		public static IHttpBufferTransport CreateDefault()
		{
#if UNITY_ANDROID
			if (!Application.isEditor)
			{
				try
				{
					return new NativeHttpTransport();
				}
				catch (Exception e)
				{
					Logger.Warning($"Native transport unavailable, using UnityWebRequest: {e.Message}");
				}
			}
#endif
			return new UnityWebRequestBufferTransport();
		}
	}
}
