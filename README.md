# TableCloth AI (식탁보 AI)

[![식탁보 AI 프로젝트 빌드 상황](https://github.com/yourtablecloth/TableClothLite/actions/workflows/gh-pages.yml/badge.svg)](https://github.com/yourtablecloth/TableClothLite/actions)

식탁보 AI 프로젝트는 [식탁보 프로젝트](https://github.com/yourtablecloth/TableCloth)의 스핀오프 프로젝트로, Blazor WebAssembly 기반의 웹 앱과 Native AOT로 컴파일되는 전용 Installer로 구성된 오픈 소스 소프트웨어입니다. 식탁보 데스크톱 앱을 설치하지 않고도 Windows Sandbox만 있으면 손쉽게 가상 환경을 만들 수 있으며, AI 채팅을 통해 금융 및 공공 부문에 관한 질문에 답변을 받을 수 있습니다.

## 주요 기능

### 🤖 AI 채팅 어시스턴트

- OpenRouter API를 활용한 AI 채팅 기능
- 금융, 공공기관, Windows Sandbox 관련 질문에 대한 답변 제공
- 대화 내역 저장 및 내보내기 기능
- 다양한 AI 모델 선택 가능

### 🛡️ Windows Sandbox 통합

- [식탁보 카탈로그](https://github.com/yourtablecloth/TableClothCatalog)에 등재된 사이트 중에서, 원하는 웹 사이트를 선택하면 Windows Sandbox 구성 파일을 생성
- Windows Sandbox 구성 파일을 다운로드하여 실행하면, 해당 웹 사이트에 필요한 플러그인을 샌드박스 내부에서 격리된 상태로 안전하게 사용 가능
- AI를 통한 자연어 기반 Sandbox 구성 파일 생성

### 💻 Progressive Web App (PWA)

- 웹 브라우저에서 바로 사용하거나 설치하여 앱처럼 사용 가능
- 오프라인 지원 (Service Worker)
- 다크/라이트 테마 지원

## 식탁보 데스크톱 버전과의 차이점

식탁보 데스크톱 버전은 컴퓨터에 저장된 공동인증서 복사 기능을 비롯한 실제 컴퓨터 환경을 인식하고 지원하는 기능들을 제공하지만, 식탁보 AI는 웹 브라우저 기반으로 동작하며 다음과 같은 특징이 있습니다:

- ✅ 설치 불필요 - 웹 브라우저에서 바로 실행
- ✅ AI 채팅 기능 - 금융/공공 부문 질문 답변
- ✅ PWA 지원 - 앱처럼 설치 및 사용 가능
- ❌ 공동인증서 복사 기능 미지원
- ❌ 로컬 시스템 환경 접근 제한

## 컨트리뷰터 가이드

### 프로젝트 개요

- **프레임워크**: Blazor WebAssembly (.NET 9)
- **주요 패키지**:
  - `OpenAI` - AI 채팅 기능
  - `Blazored.LocalStorage` - 로컬 데이터 저장
  - `Markdig` - 마크다운 렌더링
  - `AngleSharp` - HTML 파싱
- **배포**: GitHub Pages를 통한 자동 배포
- **설계 목표**: 빠른 빌드와 배포, 최소한의 설치 요구사항

### 프로젝트 구조

```
src/
├── TableClothLite/              # 메인 Blazor WebAssembly 프로젝트
│   ├── Components/              # 재사용 가능한 UI 컴포넌트
│   │   ├── Chat/               # AI 채팅 관련 컴포넌트
│   │   ├── Catalog/            # 서비스 카탈로그 컴포넌트
│   │   ├── Guide/              # 가이드 모달 컴포넌트
│   │   └── Settings/           # 설정 컴포넌트
│   ├── Pages/                  # 페이지 컴포넌트
│   ├── Services/               # 비즈니스 로직 서비스
│   └── Models/                 # 데이터 모델
├── TableClothLite.Shared/       # 공유 라이브러리
└── TableClothLite.Installer/    # Native AOT 인스톨러
```

### 개발 환경 설정

1. **.NET 9 SDK 설치**
   - [https://dotnet.microsoft.com/download/dotnet/9.0](https://dotnet.microsoft.com/download/dotnet/9.0)

2. **소스 코드 클론**
   ```bash
   git clone https://github.com/yourtablecloth/TableClothLite.git
   cd TableClothLite
   ```

3. **프로젝트 빌드 및 실행**
   ```bash
   cd src/TableClothLite
   dotnet run
   ```
   또는 Visual Studio / Visual Studio Code에서 `TableClothLite.sln` 열기

4. **개발 환경에서 실행**
   - Visual Studio: F5 또는 "디버그 시작"
   - VS Code: `dotnet watch` 명령어로 핫 리로드 지원

### 기여 가이드라인

- **이슈 또는 기능 제안** 시 구체적인 재현 방법과 요구 사항을 명확히 적어주세요.
- **코드 수정 전**에는 되도록 관련 이슈나 Pull Request를 먼저 생성합니다.
- **커밋 메시지**는 의미를 명확히 표현하고, 작은 단위로 나눠주세요.
- **코드 스타일**: C# 표준 코딩 컨벤션을 따릅니다.
- **테스트**: 주요 기능 변경 시 로컬에서 충분히 테스트해주세요.

### Pull Request 제출 방법

1. **새로운 브랜치 생성** 후 수정 사항 반영
   ```bash
   git checkout -b feature/your-feature-name
   ```

2. **로컬에서 문제없이 빌드/테스트 확인**
   ```bash
   dotnet build
   dotnet test  # (테스트가 있는 경우)
   ```

3. **메인 레포지토리로 Pull Request 생성** 시 자세한 설명 추가
   - 변경 사항의 목적과 범위
   - 관련 이슈 번호 (있는 경우)
   - 스크린샷 (UI 변경 시)

### 이슈 작성 방법

- **유형 선택**: 버그, 개선 사항, 질문, 새 기능 제안 등
- **버그 리포트**에는 다음 정보 포함:
  - 재현 스텝 (단계별로 상세히)
  - 기대 동작
  - 실제 동작
  - 환경 정보 (브라우저, OS 등)
- **기능 제안**에는 목적과 사용 사례를 구체적으로 작성

## 라이선스

이 프로젝트는 AGPL v3 기반의 오픈 소스 소프트웨어입니다. 자세한 내용은 LICENSE 파일을 참조하세요.

## 저작권 정보

<img width="100" alt="Tablecloth Icon by Icons8" src="docs/images/TableCloth_NewLogo.png" /> by [Icons8](https://img.icons8.com/color/96/000000/tablecloth.png)

<img width="100" alt="Spork Icon by Freepik Flaticon" src="docs/images/Spork_NewLogo.png" /> by [Freepik Flaticon](https://www.flaticon.com/free-icon/spork_5625701)

## 관련 링크

- 🏠 [식탁보 공식 웹사이트](https://yourtablecloth.app/)
- 💻 [식탁보 데스크톱 버전](https://github.com/yourtablecloth/TableCloth)
- 📚 [식탁보 카탈로그](https://github.com/yourtablecloth/TableClothCatalog)
- 💝 [개발 후원하기](https://github.com/sponsors/yourtablecloth)
