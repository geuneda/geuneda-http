#if UNITY_ANDROID
using System;
using Unity.Collections;
using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.Http
{
	/// <summary>
	/// Java <c>com.geuneda.http.NativeHttpChannel</c> 하나를 JNI 로 부르는 통로입니다. 메서드 ID 와 인자 배열을 만들 때 한 번 잡고,
	/// 요청·응답은 <see cref="NativeArray{T}"/> 를 감싼 Java 직접 버퍼로 주고받아 호출마다 C# 할당이 없습니다.
	/// </summary>
	internal sealed class AndroidJniHttpChannel : INativeHttpChannel
	{
		private const string ClassName = "com.geuneda.http.NativeHttpChannel";
		private const int InitialCapacity = 16 * 1024;

		private readonly AndroidJavaObject _java;
		private readonly IntPtr _object;
		private readonly IntPtr _start;
		private readonly IntPtr _poll;
		private readonly IntPtr _statusCode;
		private readonly IntPtr _responseLength;
		private readonly IntPtr _copyResponse;
		private readonly IntPtr _error;
		private readonly IntPtr _cancel;
		private readonly jvalue[] _none = new jvalue[0];
		private readonly jvalue[] _one = new jvalue[1];
		private readonly jvalue[] _two = new jvalue[2];
		private NativeArray<byte> _request;
		private NativeArray<byte> _response;
		private IntPtr _requestBuffer;
		private IntPtr _responseBuffer;

		public AndroidJniHttpChannel()
		{
			_java = new AndroidJavaObject(ClassName);

			try
			{
				_object = _java.GetRawObject();
				var type = _java.GetRawClass();
				_start = MethodId(type, "start", "(Ljava/nio/ByteBuffer;I)I");
				_poll = MethodId(type, "poll", "()I");
				_statusCode = MethodId(type, "statusCode", "()I");
				_responseLength = MethodId(type, "responseLength", "()I");
				_copyResponse = MethodId(type, "copyResponse", "(Ljava/nio/ByteBuffer;)I");
				_error = MethodId(type, "error", "()Ljava/lang/String;");
				_cancel = MethodId(type, "cancel", "()V");
			}
			catch
			{
				_java.Dispose();
				throw;
			}
		}

		public int StatusCode => AndroidJNI.CallIntMethod(_object, _statusCode, _none.AsSpan());

		public int ResponseLength => AndroidJNI.CallIntMethod(_object, _responseLength, _none.AsSpan());

		public string Error => AndroidJNI.CallStringMethod(_object, _error, _none.AsSpan());

		public void Start(byte[] frame, int length)
		{
			Reserve(ref _request, ref _requestBuffer, length);
			NativeArray<byte>.Copy(frame, _request, length);
			_two[0] = new jvalue { l = _requestBuffer };
			_two[1] = new jvalue { i = length };
			var started = AndroidJNI.CallIntMethod(_object, _start, _two.AsSpan());
			ThrowIfJavaException("start");

			if (started != 0)
			{
				throw new InvalidOperationException($"NativeHttpChannel.start failed: {started}");
			}
		}

		public int Poll() => AndroidJNI.CallIntMethod(_object, _poll, _none.AsSpan());

		public void CopyResponse(byte[] destination, int length)
		{
			if (length == 0)
			{
				return;
			}

			Reserve(ref _response, ref _responseBuffer, length);
			_one[0] = new jvalue { l = _responseBuffer };
			var copied = AndroidJNI.CallIntMethod(_object, _copyResponse, _one.AsSpan());
			ThrowIfJavaException("copyResponse");

			if (copied != length)
			{
				throw new InvalidOperationException($"NativeHttpChannel.copyResponse copied {copied} of {length}");
			}

			NativeArray<byte>.Copy(_response, destination, length);
		}

		public void Cancel() => AndroidJNI.CallVoidMethod(_object, _cancel, _none.AsSpan());

		public void Dispose()
		{
			Release(ref _request, ref _requestBuffer);
			Release(ref _response, ref _responseBuffer);
			_java.Dispose();
		}

		private static void Reserve(ref NativeArray<byte> native, ref IntPtr buffer, int length)
		{
			if (native.IsCreated && native.Length >= length)
			{
				return;
			}

			var capacity = Math.Max(length, native.IsCreated ? native.Length * 2 : InitialCapacity);
			Release(ref native, ref buffer);
			native = new NativeArray<byte>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			var local = AndroidJNI.NewDirectByteBuffer(native);

			try
			{
				ThrowIfJavaException("NewDirectByteBuffer");

				if (local == IntPtr.Zero)
				{
					throw new InvalidOperationException("NewDirectByteBuffer returned null");
				}

				buffer = AndroidJNI.NewGlobalRef(local);
				ThrowIfJavaException("NewGlobalRef");

				if (buffer == IntPtr.Zero)
				{
					throw new InvalidOperationException("NewGlobalRef returned null");
				}
			}
			catch
			{
				Release(ref native, ref buffer);
				throw;
			}
			finally
			{
				if (local != IntPtr.Zero)
				{
					AndroidJNI.DeleteLocalRef(local);
				}
			}
		}

		// 실패한 조회 뒤에 다른 JNI 호출을 하지 않게 하나씩 확인한다
		private static IntPtr MethodId(IntPtr type, string name, string signature)
		{
			var id = AndroidJNI.GetMethodID(type, name, signature);
			ThrowIfJavaException(name);

			if (id == IntPtr.Zero)
			{
				throw new InvalidOperationException($"NativeHttpChannel.{name}{signature} not found");
			}

			return id;
		}

		private static void Release(ref NativeArray<byte> native, ref IntPtr buffer)
		{
			if (buffer != IntPtr.Zero)
			{
				AndroidJNI.DeleteGlobalRef(buffer);
				buffer = IntPtr.Zero;
			}

			if (native.IsCreated)
			{
				native.Dispose();
			}
		}

		private static void ThrowIfJavaException(string call)
		{
			var exception = AndroidJNI.ExceptionOccurred();

			if (exception == IntPtr.Zero)
			{
				return;
			}

			AndroidJNI.ExceptionClear();
			AndroidJNI.DeleteLocalRef(exception);
			throw new InvalidOperationException($"NativeHttpChannel.{call} threw a Java exception");
		}
	}
}
#endif
