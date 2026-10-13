# Changelog
이 패키지의 모든 주요 변경사항은 이 파일에 기록됩니다.

이 형식은 [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)를 기반으로 하며,
이 프로젝트는 [Semantic Versioning](http://semver.org/spec/v2.0.0.html)을 따릅니다.

## [Unreleased]

**New**:
- `HttpClient`(`IHttpClient`)와 `HttpClientBuilder`: 기본 주소, 미들웨어 `(order, 등록 순서)` 체인, 교체 가능한 전송, JSON 설정
- 결과는 `Result<HttpResponse>`: 응답이 오면(4xx, 5xx 포함) 성공, 연결 실패 `NetworkError`, 타임아웃 `Timeout`, 취소 `Cancelled`(예외 아님)
- JSON 편의 메서드 `GetJsonAsync<T>`, `PostJsonAsync`, `PostJsonAsync<T>`, `SendJsonAsync<T>`(2xx 가 아니면 `HttpResultCodes.HttpError`, 해석 실패 `InvalidResponse`)
- 바꾸지 않는 `HttpRequest`(`With*` 복사본)와 `HttpResponse`
- `UnityWebRequestTransport`: 어느 스레드에서든 메인 스레드로 옮겨 보내고, 타임아웃·취소 때 요청 중단
- 선택형 미들웨어 `TimeoutMiddleware`, `RetryMiddleware`(멱등 메서드, 일시 실패, 지수 백오프 + 지터), `DefaultHeadersMiddleware`
- 결과 코드 `HttpResultCodes`(1000 블록)
- 의존성: `com.geuneda.core`, `com.cysharp.unitask`, `com.unity.nuget.newtonsoft-json`, `com.unity.modules.unitywebrequest`
