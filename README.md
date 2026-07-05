# 무설치 식탁보 (TableCloth Lite)

[![식탁보 Lite 프로젝트 빌드 상황](https://github.com/yourtablecloth/TableClothLite/actions/workflows/gh-pages.yml/badge.svg)](https://github.com/yourtablecloth/TableClothLite/actions)

무설치 식탁보는 [식탁보 프로젝트](https://github.com/yourtablecloth/TableCloth)의 스핀오프로, Blazor WebAssembly 기반의 웹 앱입니다. 식탁보 데스크톱 앱을 **설치하지 않고도** 웹 브라우저와 Windows Sandbox만으로, 은행·공공 웹 사이트를 격리된 일회용 환경에서 안전하게 이용할 수 있는 **무설치 실행(.wsb) 파일**을 만들어 줍니다.

> **🤖 AI 기능 관련 공지**
>
> 이전 버전에 있던 **OpenRouter 기반 AI 채팅 기능은 제거되었습니다.** AI 기능은 웹 채팅과는 **다른 형태로 다시 소개될 예정**입니다. 그동안 무설치 식탁보는 본연의 목적인 "무설치 Windows Sandbox 실행"에 집중합니다.

## 동작 방식

무설치 식탁보는 [식탁보 저장소의 파라미터화 `.wsb` 스펙](https://github.com/yourtablecloth/TableCloth/blob/main/docs/PARAMETERIZED_WSB_SPEC.md)을 따르는 **Express(빠른 실행) 레인**의 소비자(웹앱)입니다.

1. 웹 앱에서 **무설치 실행 파일(.wsb)** 을 내려받습니다. (일반 런처 또는 특정 사이트 사전 선택)
2. `.wsb` 파일을 더블클릭하면 **Windows Sandbox**가 열립니다.
3. 샌드박스 안에서 보이는 준비 창이 뜨고, 고정 URL의 준비 스크립트(`tablecloth-prepare.ps1`)를 받아 실행합니다(무파일 `iex`). 준비 스크립트가 공식 GitHub 릴리스의 서명된 무설치 런처(`SporkBootstrap_{arch}.exe`)를 받아 실행하고, 런처가 **최신 포터블 식탁보(Spork)** 를 내려받아 실행합니다.

생성되는 `.wsb`는 다음 특성을 가집니다.

- **폴더 마운트 없음** — 호스트 PC의 파일에 전혀 접근하지 않아, 유출 벡터를 구조적으로 차단합니다.
- **아키텍처 자동 판별** — 준비 스크립트/런처가 게스트 런타임에서 x64/arm64를 판별합니다.
- **버전 고정 불필요** — GitHub `releases/latest` 고정 URL을 사용해 항상 최신 릴리스를 실행합니다.
- **`.wsb` 재배포 불필요** — 부트스트랩 로직은 릴리스 자산인 준비 스크립트가 담당하므로, 로직 변경이 `.wsb` 수정 없이 다음 릴리스부터 반영됩니다.
- **ASCII 전용 LogonCommand** — Windows PowerShell 5.1의 코드페이지 문제를 피하기 위해 명령을 영문 ASCII로만 구성합니다(현지화는 런처 GUI가 담당).
- **사이트 딥링크** — 카탈로그에서 사이트를 선택하면 `.wsb`가 환경 변수 `TABLECLOTH_SITE_IDS`로 사이트 Id를 전달해 자동으로 열립니다.

## 주요 기능

### 🛡️ 무설치 Windows Sandbox 실행

- [식탁보 카탈로그](https://github.com/yourtablecloth/TableClothCatalog)에 등재된 사이트를 선택해 무설치 `.wsb` 생성
- `.wsb` 하나로 Windows Sandbox 안에서 필요한 플러그인을 격리된 상태로 안전하게 사용

### 💻 Progressive Web App (PWA)

- 웹 브라우저에서 바로 사용하거나 설치하여 앱처럼 사용 가능
- 오프라인 지원 (Service Worker)
- 다크/라이트 테마 지원

## 식탁보 데스크톱 버전과의 차이점

식탁보 데스크톱 버전은 공동인증서 복사 등 실제 컴퓨터 환경을 인식·지원하는 기능을 제공합니다. 무설치 식탁보는 웹 브라우저 기반으로 동작하며 다음과 같은 특징이 있습니다.

- ✅ 설치 불필요 — 웹 브라우저에서 바로 실행
- ✅ 무설치 `.wsb` 생성 — 데스크톱 앱 설치 없이 Windows Sandbox에서 식탁보 실행
- ✅ PWA 지원 — 앱처럼 설치 및 사용 가능
- ❌ 공동인증서 복사 기능 미지원 (마운트 없음 → 모바일 인증 권장)
- ❌ 로컬 시스템 환경 접근 제한

## 컨트리뷰터 가이드

### 프로젝트 개요

- **프레임워크**: Blazor WebAssembly (.NET 9)
- **주요 패키지**:
  - `Blazored.LocalStorage` - 로컬 데이터 저장
- **배포**: GitHub Pages를 통한 자동 배포
- **설계 목표**: 빠른 빌드와 배포, 최소한의 설치 요구사항

### 프로젝트 구조

```text
src/
├── TableClothLite/              # 메인 Blazor WebAssembly 프로젝트
│   ├── Components/              # 재사용 가능한 UI 컴포넌트
│   │   ├── Catalog/            # 서비스 카탈로그 컴포넌트
│   │   ├── Chat/               # 테마 토글 등 공용 UI
│   │   ├── Guide/             # 가이드 모달 컴포넌트
│   │   └── Settings/           # 설정 컴포넌트
│   ├── Pages/                  # 페이지 컴포넌트
│   ├── Services/               # 비즈니스 로직 서비스
│   │   ├── SandboxComposerService.cs  # 무설치 .wsb 생성 (Express 스펙)
│   │   └── SandboxService.cs           # 카탈로그·다운로드 오케스트레이션
│   └── Models/                 # 데이터 모델
├── TableClothLite.Shared/       # 공유 라이브러리
└── TableClothLite.Installer/    # Native AOT 인스톨러
```

### 개발 환경 설정

1. **.NET 9 SDK 설치**
   - [https://dotnet.microsoft.com/download/dotnet/9.0](https://dotnet.microsoft.com/download/dotnet/9.0)

2. **소스 코드 클론** (서브모듈 포함)
   ```bash
   git clone --recurse-submodules https://github.com/yourtablecloth/TableClothLite.git
   cd TableClothLite
   ```

3. **프로젝트 빌드 및 실행**
   ```bash
   cd src/TableClothLite
   dotnet run
   ```
   또는 Visual Studio / Visual Studio Code에서 솔루션 열기

### 기여 가이드라인

- **이슈 또는 기능 제안** 시 구체적인 재현 방법과 요구 사항을 명확히 적어주세요.
- **코드 수정 전**에는 되도록 관련 이슈나 Pull Request를 먼저 생성합니다.
- **커밋 메시지**는 의미를 명확히 표현하고, 작은 단위로 나눠주세요.
- **코드 스타일**: C# 표준 코딩 컨벤션을 따릅니다.

## 라이선스

이 프로젝트는 AGPL v3 기반의 오픈 소스 소프트웨어입니다. 자세한 내용은 LICENSE 파일을 참조하세요.

## 저작권 정보

<img width="100" alt="Tablecloth Icon by Icons8" src="docs/images/TableCloth_NewLogo.png" /> by [Icons8](https://img.icons8.com/color/96/000000/tablecloth.png)

<img width="100" alt="Spork Icon by Freepik Flaticon" src="docs/images/Spork_NewLogo.png" /> by [Freepik Flaticon](https://www.flaticon.com/free-icon/spork_5625701)

## 관련 링크

- 🏠 [식탁보 공식 웹사이트](https://yourtablecloth.app/)
- 💻 [식탁보 데스크톱 버전](https://github.com/yourtablecloth/TableCloth)
- 📚 [식탁보 카탈로그](https://github.com/yourtablecloth/TableClothCatalog)
- 📄 [파라미터화 `.wsb` 스펙](https://github.com/yourtablecloth/TableCloth/blob/main/docs/PARAMETERIZED_WSB_SPEC.md)
- 💝 [개발 후원하기](https://github.com/sponsors/yourtablecloth)
