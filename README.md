# Geuneda Http

UnityWebRequest 기반 HTTP 클라이언트입니다. 공통 처리는 미들웨어 체인에 두고, 결과는 core 결과 규약(`Result`)을 따릅니다.

| 기능 | 타입 | 네임스페이스 |
|------|------|-------------|
| 클라이언트 | `IHttpClient`, `HttpClient`, `HttpClientBuilder` | `Geuneda.Http` |
| 요청·응답 | `HttpRequest`, `HttpResponse`, `HttpMethod` | `Geuneda.Http` |
| 미들웨어 | `IHttpMiddleware`, `HttpHandler`, `TimeoutMiddleware`, `RetryMiddleware`, `DefaultHeadersMiddleware` | `Geuneda.Http` |
| 전송 | `IHttpTransport`, `UnityWebRequestTransport` | `Geuneda.Http` |
| 결과 코드 | `HttpResultCodes`(1000 블록) | `Geuneda.Http` |

## 설치

`Packages/manifest.json` 에 추가합니다. core 와 UniTask 는 git 의존성이라 함께 넣어야 합니다. Newtonsoft Json 은 Unity 레지스트리에서 자동으로 설치됩니다.

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.geuneda.core": "https://github.com/geuneda/geuneda-core.git",
    "com.geuneda.http": "https://github.com/geuneda/geuneda-http.git"
  }
}
```

## 사용법

```csharp
using System;
using System.Collections.Generic;
using Geuneda.Core;
using Geuneda.Http;

// 부트스트랩: 한 번 만들어 인터페이스로 바인딩한다
var client = new HttpClientBuilder()
    .WithBaseAddress(gameConfig.ServerUrl)                    // 예: https://api.example.com/v1/ (끝을 / 로)
    .AddMiddleware(new DefaultHeadersMiddleware(new Dictionary<string, string> { ["X-App-Version"] = Application.version }))
    .AddMiddleware(new TimeoutMiddleware(TimeSpan.FromSeconds(10)))
    .AddMiddleware(new RetryMiddleware())
    .Build();
MainInstaller.Bind<IHttpClient>(client);

// 사용하는 쪽
var result = await http.GetJsonAsync<Profile>("profiles/me", token);
if (result.TryGetValue(out var profile))
{
    Show(profile);
}
else if (result.Code == ResultCodes.NetworkError || result.Code == ResultCodes.Timeout)
{
    ShowRetryPopup();
}
```

### 결과

| 상황 | 결과 |
|------|------|
| 응답이 옴(2xx, 3xx 처리 후, 4xx, 5xx 모두) | 성공 `Result<HttpResponse>`. `StatusCode`, `Body`, `Headers`, `IsSuccessStatusCode` |
| 연결 실패(서버 없음, DNS, 오프라인) | `ResultCodes.NetworkError` |
| 요청의 제한 시간 초과 | `ResultCodes.Timeout` |
| 호출자 취소 | `ResultCodes.Cancelled`(예외 아님) |
| 내려받기 처리 오류 | `HttpResultCodes.InvalidResponse` |
| 잘못된 주소, 기본 주소 없는 상대 주소, 잘못된 헤더 | 즉시 예외(오용) |

- 4xx, 5xx 를 응답으로 돌려주는 것은 미들웨어가 401(토큰 재발급 후 재시도), 403(지역 제한) 같은 상태를 한곳에서 처리할 수 있게 하려는 것입니다.
- JSON 편의 메서드(`GetJsonAsync<T>`, `PostJsonAsync<T>`, `SendJsonAsync<T>`)는 2xx 가 아니면 `HttpResultCodes.HttpError`(메시지에 상태 코드), 본문이 비었거나 공백뿐이거나 JSON 이 아니면 `HttpResultCodes.InvalidResponse` 입니다. 본문이 JSON 리터럴 `null` 이면 참조 형식과 nullable 값 형식(`int?`)은 성공에 값 null, nullable 이 아닌 값 형식(`int`)은 `InvalidResponse` 입니다. 오류 본문이 필요하면 `SendAsync` 로 응답을 직접 봅니다.
- `HttpRequest` 는 바꾸지 않는 객체입니다. `HttpRequest.Put("items/3").WithJsonBody(item).WithHeader("X-Key", key).WithTimeout(TimeSpan.FromSeconds(5))` 처럼 복사본을 만듭니다.
- 어느 스레드에서 불러도 됩니다. 전송은 메인 스레드로 옮겨 UnityWebRequest 를 보냅니다. 옮기는 동안 취소되면 요청을 보내지 않습니다.
- 본문이 있는 요청의 Content-Type 은 `WithBody`/`WithJsonBody` 로 정한 값입니다. 헤더 사전의 Content-Type(직접 넣었거나 `DefaultHeadersMiddleware` 기본값)은 본문이 있으면 쓰지 않습니다.
- 헤더 사전은 읽기 전용입니다. 바꾸려면 `WithHeader` 로 복사본을 만듭니다.

### 미들웨어

```csharp
public sealed class SessionHeaderMiddleware : IHttpMiddleware
{
    private readonly ISessionService _session;

    public SessionHeaderMiddleware(ISessionService session)
    {
        _session = session;
    }

    public UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, HttpHandler next, CancellationToken cancellationToken)
    {
        return next(request.WithHeader("X-Session-Id", _session.Id), cancellationToken);
    }
}
```

- `AddMiddleware(middleware, order)`: order 가 작을수록 바깥(먼저 실행)이고, 같으면 추가한 순서입니다. 기본 order 는 0 입니다.
- 요청을 바꾸려면 `With*` 복사본을 `next` 에 넘깁니다. 같은 요청을 다시 보내려면 `next` 를 다시 부릅니다(Percent 의 체인 되감기 대신).
- 미들웨어 안에서 취소를 예외로 던져도, 호출자 토큰이 취소된 경우 클라이언트가 `Cancelled` 결과로 바꿉니다.

| 기본 제공 | 동작 |
|-----------|------|
| `TimeoutMiddleware(timeout)` | 제한 시간이 없는 요청에 붙입니다. 시도마다 적용됩니다 |
| `RetryMiddleware(maxRetries = 3, baseDelay = 0.5초, maxDelay = 8초)` | GET, HEAD, PUT, DELETE 가 연결 실패, 타임아웃, 408, 429, 500, 502, 503, 504 이면 다시 보냅니다. 대기는 `min(maxDelay, baseDelay × 2^n)` 의 50~100%. 취소, 다른 4xx, POST, PATCH 는 다시 보내지 않습니다. `Retry-After` 는 보지 않습니다 |
| `DefaultHeadersMiddleware(headers)` | 없는 헤더만 붙입니다. 본문 Content-Type 이 있으면 Content-Type 기본값은 붙이지 않습니다 |

타임아웃과 재시도는 기본으로 켜져 있지 않습니다. 필요한 클라이언트에 미들웨어로 추가합니다.

### HTTP 주소

Player Settings 의 **Allow downloads over HTTP** 가 꺼져 있으면 플레이어에서 `http://` 요청이 막힐 수 있습니다. 운영 서버는 `https://` 를 쓰세요. 에디터에서는 이 설정이 꺼져 있어도 `http://127.0.0.1` 요청이 동작하는 것을 PlayMode 테스트로 확인했습니다.

## 요구 사항

- Unity 2021.3 이상
- `com.geuneda.core` 0.1.0, `com.cysharp.unitask` 2.5.10, `com.unity.nuget.newtonsoft-json` 3.2.1, `com.unity.modules.unitywebrequest` 1.0.0

변경 이력은 [CHANGELOG.md](CHANGELOG.md) 를 참고하세요.

## 라이선스

MIT. [LICENSE.md](LICENSE.md) 를 참고하세요.
