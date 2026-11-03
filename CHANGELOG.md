# Changelog
이 패키지의 모든 주요 변경사항은 이 파일에 기록됩니다.

이 형식은 [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)를 기반으로 하며,
이 프로젝트는 [Semantic Versioning](http://semver.org/spec/v2.0.0.html)을 따릅니다.

## [0.2.0] - 2026-11-03

**New**:
- 버퍼 전송 `IHttpBufferTransport`: 다시 쓰는 `HttpBufferRequest`(헤더·본문 버퍼)와 `HttpBufferResponse`(본문 버퍼)로 보내 반복 요청의 C# 할당을 없앤다. 결과 규약은 `IHttpTransport` 와 같다. `IDisposable` 이라 만든 쪽이 닫는다
- `NativeHttpTransport`(안드로이드 플레이어): Java 작업 스레드의 `HttpURLConnection`, `NativeArray` 직접 버퍼, 미리 잡은 JNI 메서드 ID, 매 프레임 상태 숫자 폴링. 전체 제한 시간·호출자 취소 때 네이티브 요청을 끊고, 동시 요청 수만큼 통로를 다시 쓴다. 네이티브가 거절한 주소·헤더는 `ArgumentException`(대체 구현과 같음)
- `UnityWebRequestBufferTransport`(에디터·iOS 대체), `HttpBufferTransports.CreateDefault()`(플랫폼 선택, Java 플러그인이 없으면 대체)
- Android 라이브러리 `Runtime/Plugins/Android/GeunedaHttp.androidlib`(의존성 없음, consumer ProGuard 규칙)
- Java 채널 테스트 `Tests/Java~/run.sh`(데스크톱 JDK, 로컬 HttpServer)
- 의존성 추가: `com.unity.modules.androidjni`

## [0.1.0] - 2026-10-13

**New**:
- `HttpClient`(`IHttpClient`)와 `HttpClientBuilder`: 기본 주소, 미들웨어 `(order, 등록 순서)` 체인, 교체 가능한 전송, JSON 설정
- 결과는 `Result<HttpResponse>`: 응답이 오면(4xx, 5xx 포함) 성공, 연결 실패 `NetworkError`, 타임아웃 `Timeout`, 취소 `Cancelled`(예외 아님)
- JSON 편의 메서드 `GetJsonAsync<T>`, `PostJsonAsync`, `PostJsonAsync<T>`, `SendJsonAsync<T>`(2xx 가 아니면 `HttpResultCodes.HttpError`, 해석 실패 `InvalidResponse`)
- 바꾸지 않는 `HttpRequest`(`With*` 복사본)와 `HttpResponse`
- `UnityWebRequestTransport`: 어느 스레드에서든 메인 스레드로 옮겨 보내고, 타임아웃·취소 때 요청 중단
- 선택형 미들웨어 `TimeoutMiddleware`, `RetryMiddleware`(멱등 메서드, 일시 실패, 지수 백오프 + 지터), `DefaultHeadersMiddleware`
- 결과 코드 `HttpResultCodes`(1000 블록)
- 의존성: `com.geuneda.core`, `com.cysharp.unitask`, `com.unity.nuget.newtonsoft-json`, `com.unity.modules.unitywebrequest`
