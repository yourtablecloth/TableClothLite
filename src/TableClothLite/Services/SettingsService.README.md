# SettingsService 사용 가이드

## 개요
`SettingsService`는 Blazored.LocalStorage를 활용하여 애플리케이션 설정을 관리하는 서비스입니다.
`SandboxSettingsModel`을 주입받는 대신, 이 서비스를 통해 설정을 로드하고 저장할 수 있습니다.

## 등록
`Program.cs`에서 Singleton으로 등록되어 있습니다:
```csharp
builder.Services.AddSingleton<SettingsService>();
```

## 주요 기능

### 1. 설정 가져오기
```csharp
[Inject] private SettingsService SettingsService { get; set; } = default!;

var settings = await SettingsService.GetSettingsAsync();
Console.WriteLine($"네트워크 활성화: {settings.EnableNetworking}");
```

### 2. 설정 저장하기
```csharp
var settings = await SettingsService.GetSettingsAsync();
settings.EnableNetworking = false;
await SettingsService.SaveSettingsAsync(settings);
```

### 3. 설정 업데이트하기 (간편한 방법)
```csharp
await SettingsService.UpdateSettingsAsync(settings => 
{
    settings.EnableNetworking = false;
    settings.EnableAudioInput = true;
});
```

### 4. 기본값으로 초기화
```csharp
await SettingsService.ResetToDefaultsAsync();
```

### 5. 설정 새로고침
```csharp
// 캐시를 무효화하고 LocalStorage에서 다시 로드
await SettingsService.RefreshSettingsAsync();
```

## 설정 변경 이벤트

설정이 변경될 때마다 알림을 받을 수 있습니다:

```csharp
protected override void OnInitialized()
{
    SettingsService.SettingsChanged += OnSettingsChanged;
}

private void OnSettingsChanged(object? sender, SandboxSettingsModel settings)
{
    Console.WriteLine("설정이 변경되었습니다!");
    StateHasChanged();
}

public void Dispose()
{
    SettingsService.SettingsChanged -= OnSettingsChanged;
}
```

## SettingsModal.razor 사용 예제

```razor
@inject SettingsService SettingsService

@code {
    private SandboxSettingsModel _settingsModel = new();

    protected override async Task OnInitializedAsync()
    {
        _settingsModel = await SettingsService.GetSettingsAsync();
    }

    private async Task SaveSettings()
    {
        await SettingsService.SaveSettingsAsync(_settingsModel);
    }
}
```

## 내부 구조

- **캐싱**: 첫 로드 후 메모리에 캐시되어 빠른 접근 가능
- **Thread-Safe**: `SemaphoreSlim`을 사용하여 동시성 보장
- **자동 초기화**: 설정이 없으면 자동으로 기본값 생성
- **이벤트 기반**: 설정 변경 시 구독자에게 알림

## 장점

1. **중앙 집중식 관리**: 모든 설정이 하나의 서비스에서 관리됨
2. **자동 영속화**: LocalStorage를 통해 브라우저를 닫아도 설정 유지
3. **타입 안전성**: `SandboxSettingsModel`을 통한 강타입 지원
4. **변경 감지**: 이벤트를 통해 설정 변경을 추적할 수 있음
5. **캐싱**: 불필요한 LocalStorage 접근 최소화

## 마이그레이션 가이드

### 이전 방식 (주입)
```csharp
[Inject] private SandboxSettingsModel SettingsModel { get; set; } = default!;
[Inject] private ConfigService ConfigService { get; set; } = default!;

// 로드
var config = await ConfigService.LoadAsync();
SettingsModel.FromSandboxConfig(config);

// 저장
var config = SettingsModel.ToSandboxConfig();
await ConfigService.SaveAsync(config);
```

### 새로운 방식 (SettingsService)
```csharp
[Inject] private SettingsService SettingsService { get; set; } = default!;

// 로드
var settings = await SettingsService.GetSettingsAsync();

// 저장
await SettingsService.SaveSettingsAsync(settings);
```

## 주의사항

- `SettingsService`는 Singleton이므로 애플리케이션 전체에서 공유됩니다
- LocalStorage는 브라우저별로 독립적이므로 다른 브라우저에서는 설정이 공유되지 않습니다
- 캐시된 설정을 변경하고 싶다면 `RefreshSettingsAsync()`를 호출하세요
