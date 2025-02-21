# TableClothLite

[![식탁보 라이트 프로젝트 빌드 상황](https://github.com/yourtablecloth/TableClothLite/actions/workflows/gh-pages.yml/badge.svg)](https://github.com/yourtablecloth/TableClothLite/actions)

식탁보 라이트 프로젝트는 [식탁보 프로젝트](https://github.com/yourtablecloth/TableCloth)의 스핀오프 프로젝트로, Blazor WebAssembly 기반의 웹 앱과 Native AOT로 컴파일되는 전용 Installer로 구성된 오픈 소스 소프트웨어입니다. 식탁보 데스크톱 앱을 설치하지 않고도 Windows Sandbox만 있으면 손쉽게 가상 환경을 만들 수 있는 것이 특징입니다.

## 기능

- [식탁보 카탈로그](https://github.com/yourtablecloth/TableClothCatalog)에 등재된 사이트 중에서, 원하는 웹 사이트를 선택하면 Windows Sandbox 구성 파일을 만들어 줍니다.
- Windows Sandbox 구성 파일을 다운로드 받아서 Windows Sandbox에서 실행하면, 해당 웹 사이트에서 필요로 하는 플러그인을 샌드박스 내부에서 격리된 상태로 안전하게 탐색할 수 있습니다.

## 식탁보 데스크톱 버전과의 차이점

식탁보 데스크톱 버전은 컴퓨터에 저장된 공동인증서 복사 기능을 비롯한 실제 컴퓨터 환경을 인식하고 지원하는 기능들을 제공하지만, 식탁보 라이트는 웹 브라우저에서 알 수 없는 정보는 Sandbox 구성 파일로 만드는 기능을 제공하지 않습니다.

## 컨트리뷰터 가이드

### 프로젝트 개요

- Blazor(.NET 9) 기반
- WebAssembly 사용
- 빠른 빌드와 배포를 목표로 설계됨

### 개발 환경 설정

1. 1. .NET 9 SDK 설치
2. 소스 코드 클론 후 Blazor WebAssembly 프로젝트 열기
3. Visual Studio에서 빌드 및 실행

### 기여 가이드라인

- - 이슈 또는 기능 제안 시 구체적인 재현 방법과 요구 사항을 명확히 적어주세요.
- 코드 수정 전에는 되도록 관련 이슈나 Pull Request를 먼저 생성합니다.
- 커밋 메시지는 의미를 명확히 표현하고, 작은 단위로 나눠주세요.

### Pull Request 제출 방법

1. 1. 새로운 브랜치 생성 후 수정 사항 반영
2. 로컬에서 문제없이 빌드/테스트 확인
3. 메인 레포지토리로 Pull Request 생성 시 자세한 설명 추가

### 이슈 작성 방법

- 버그, 개선 사항, 질문 등에 맞춰 유형 선택
- 재현 스텝과 기대 동작을 구체적으로 작성

## 저작권 정보

<img width="100" alt="Tablecloth Icon by Icons8" src="docs/images/TableCloth_NewLogo.png" /> by [Icons8](https://img.icons8.com/color/96/000000/tablecloth.png)

<img width="100" alt="Spork Icon by Freepik Flaticon" src="docs/images/Spork_NewLogo.png" /> by [Freepik Flaticon](https://www.flaticon.com/free-icon/spork_5625701)
