# Geuneda.Http - AI 에이전트 가이드

> **관련 파일**: `CLAUDE.md`는 이 파일을 감쌉니다 — `CLAUDE.md`가 아니라 `AGENTS.md`를 편집하세요. `README.md`는 사용자 대상 문서입니다.

## 1. 패키지 개요
- **패키지**: `com.geuneda.http`
- **Unity**: 2021.3+
- **의존성**: `com.geuneda.core`(Result, ResultCodes, ModuleLogger), `com.cysharp.unitask`, `com.unity.nuget.newtonsoft-json`, `com.unity.modules.unitywebrequest`
- **출처**: Lucky Defense 의 Percent PercentHttp 를 참고했습니다(geuneda 로드맵 3-1). 미들웨어 `(order, 등록 순서)` 체인과 "HTTP 오류도 응답으로" 구조는 따르고, 연결 실패(Percent 는 상태 코드 0)·타임아웃·취소는 `Result` 코드로 나눴습니다.

## 2. 규칙

- **결과 규약**: 응답이 오면(4xx, 5xx 포함) 성공 `Result<HttpResponse>`, 응답이 없으면 `NetworkError`/`Timeout`/`Cancelled`. 오용(잘못된 주소·헤더, 기본 주소 없는 상대 주소)은 예외. 이 경계를 바꾸지 마세요. 미들웨어가 401·403 을 응답으로 받아야 재발급·재시도를 한곳에서 처리할 수 있습니다.
- `HttpRequest`, `HttpResponse` 는 바꾸지 않는 객체입니다. 헤더 사전은 `ReadOnlyDictionary` 로 감싸 내부 사전을 노출하지 않습니다(공유하는 빈 사전 포함). 메서드는 생성자에서 정의된 enum 값만 받습니다. 미들웨어는 `With*` 복사본을 다음 단계로 넘깁니다. 재시도는 `next` 를 다시 부르는 것으로 합니다(인덱스 되감기 없음).
- 체인은 `HttpClientBuilder.Build` 에서 한 번 조립합니다. order 오름차순, 같으면 등록 순서(안정 정렬).
- `UnityWebRequestTransport` 는 UniTask 의 `ToUniTask`(오류면 예외)를 쓰지 않고 `isDone` 을 기다린 뒤 `UnityWebRequest.result` 로 나눕니다. 타임아웃은 연결된 `CancellationTokenSource.CancelAfter` 로 하고, 호출자 토큰이 취소됐으면 `Cancelled`, 아니면 `Timeout` 입니다. 두 경우 모두 `Abort` 합니다. 메인 스레드로 옮긴 직후 취소를 다시 확인해, 취소됐으면 보내지 않습니다. 본문이 있으면 Content-Type 은 `HttpRequest.ContentType` 만 쓰고 헤더 사전의 Content-Type 은 건너뜁니다(`DefaultHeadersMiddleware` 도 본문 Content-Type 을 있는 헤더로 봄).
- 타임아웃·재시도는 기본으로 켜지 않습니다(사용자 결정 2026-10-13). 미들웨어로만 켭니다.
- `RetryMiddleware` 는 멱등 메서드만, 일시 실패(연결 실패, 타임아웃, 408/429/500/502/503/504)만 다시 보냅니다. 취소는 다시 보내지 않습니다. 대기 함수와 난수는 internal 생성자로 바꿔 테스트합니다.
- 결과 코드는 1000 블록(`HttpResultCodes`, core README 의 범위 표에 등록). 값은 바꾸거나 재사용하지 않습니다.
- static 가변 상태 금지(D2). 클라이언트는 인스턴스로 만들어 `MainInstaller.Bind<IHttpClient>` 로 바인딩합니다.

## 3. 테스트

- `Tests/EditMode/` (`Geuneda.Http.Tests.EditMode`): `FakeTransport` 로 체인 순서, 주소, 결과 변환, JSON, 미들웨어. 비동기 흐름이 동기적으로 끝나는지 `FakeTransport.Run` 이 확인합니다.
- `Tests/PlayMode/` (`Geuneda.Http.Tests.PlayMode`): 로컬 `HttpListener`(127.0.0.1, 빈 포트)로 실제 UnityWebRequest 의 상태 코드, POST 본문, 연결 실패, 타임아웃, 취소, 백그라운드 스레드 호출을 확인합니다. 프로젝트의 HTTP 허용 설정이 꺼져 있어도 에디터에서 `http://127.0.0.1` 이 동작하는지는 이 테스트로 확인합니다.
