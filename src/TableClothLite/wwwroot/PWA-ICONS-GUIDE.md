# PWA 아이콘 생성 가이드

이 프로젝트는 PWA(Progressive Web App)로 설치 가능하도록 구성되어 있습니다.

## 필요한 아이콘 파일

PWA가 제대로 작동하려면 다음 아이콘 파일들이 필요합니다:

### 필수 아이콘
- `icon-192.png` (192x192px) - Android Chrome, 홈 화면 아이콘
- `icon-512.png` (512x512px) - Android Chrome, 스플래시 스크린
- `favicon.png` - 브라우저 탭 아이콘

### 선택적 아이콘 (권장)
- `icon-144.png` (144x144px) - Windows 타일
- `icon-96.png` (96x96px) - 일부 Android 기기
- `icon-72.png` (72x72px) - iOS Safari 구형 기기
- `icon-48.png` (48x48px) - Windows 작업 표시줄

## 아이콘 생성 방법

### 1. 온라인 도구 사용 (추천)

#### PWA Builder
1. https://www.pwabuilder.com/imageGenerator 방문
2. 원본 이미지 업로드 (최소 512x512px, PNG 권장)
3. "Generate" 클릭
4. 생성된 모든 아이콘을 `wwwroot/` 폴더에 복사

#### RealFaviconGenerator
1. https://realfavicongenerator.net/ 방문
2. 원본 이미지 업로드
3. PWA 옵션 선택
4. 생성된 아이콘 다운로드 및 배치

### 2. 수동 생성 (디자이너)

Figma, Photoshop, GIMP 등을 사용하여:
1. 512x512px 원본 이미지 제작
2. 각 크기별로 리사이즈하여 저장
3. PNG 형식으로 저장 (투명 배경 지원)

### 3. 명령줄 도구 (개발자)

ImageMagick 설치 후:

```bash
# 512x512 원본에서 다양한 크기 생성
convert icon-512.png -resize 192x192 icon-192.png
convert icon-512.png -resize 144x144 icon-144.png
convert icon-512.png -resize 96x96 icon-96.png
convert icon-512.png -resize 72x72 icon-72.png
convert icon-512.png -resize 48x48 icon-48.png
convert icon-512.png -resize 32x32 favicon.png
```

## 아이콘 디자인 가이드라인

### Safe Zone (안전 영역)
- 중요한 콘텐츠는 중앙 80% 영역에 배치
- 가장자리 10%는 마스킹에 대비하여 여백 유지

### 색상
- 배경색: 매니페스트의 `background_color`와 일치
- 전경색: 명확한 대비 유지
- 투명 배경보다는 불투명 배경 권장 (일부 플랫폼 호환성)

### 형태
- 정사각형 1:1 비율
- 심플하고 인식하기 쉬운 디자인
- 텍스트는 최소화 (읽기 어려움)

### Maskable Icon (선택적)
- 모든 플랫폼의 다양한 모양(원형, 모서리 둥근 정사각형 등)에 대응
- Safe Zone 준수 필수

## 테스트 방법

### 1. 로컬 테스트
```bash
dotnet run
# 또는
dotnet watch run
```

### 2. Lighthouse 테스트
1. Chrome DevTools 열기 (F12)
2. "Lighthouse" 탭 선택
3. "Progressive Web App" 체크
4. "Generate report" 클릭

### 3. PWA 설치 테스트
- Chrome: 주소창 오른쪽의 "설치" 아이콘 클릭
- Edge: 주소창 오른쪽의 "앱 설치" 클릭
- Safari (iOS): 공유 버튼 → "홈 화면에 추가"

## 매니페스트 파일

아이콘 정보는 `wwwroot/manifest.webmanifest`에 정의되어 있습니다:

```json
{
  "icons": [
    {
      "src": "icon-192.png",
      "sizes": "192x192",
      "type": "image/png",
      "purpose": "any"
    },
    {
      "src": "icon-512.png",
      "sizes": "512x512",
      "type": "image/png",
      "purpose": "maskable"
    }
  ]
}
```

## 문제 해결

### 아이콘이 표시되지 않는 경우
1. 브라우저 캐시 삭제
2. Service Worker 업데이트 확인
3. 파일 경로 확인 (`wwwroot/` 폴더)
4. 파일명 대소문자 확인

### PWA 설치 프롬프트가 나타나지 않는 경우
1. HTTPS 연결 확인 (localhost 제외)
2. 매니페스트 파일 유효성 검사
3. Service Worker 등록 확인
4. 최소 요구사항 충족 확인 (아이콘, name, start_url 등)

## 추가 리소스

- [PWA Builder](https://www.pwabuilder.com/)
- [MDN PWA Guide](https://developer.mozilla.org/en-US/docs/Web/Progressive_web_apps)
- [Google PWA Checklist](https://web.dev/pwa-checklist/)
- [Maskable Icon Editor](https://maskable.app/editor)

## 현재 프로젝트 상태

✅ PWA 매니페스트 파일 (`manifest.webmanifest`)
✅ Service Worker (`service-worker.js`)
✅ 오프라인 페이지 (`offline.html`)
✅ PWA 설치 프롬프트 컴포넌트
✅ PWA 업데이트 알림 컴포넌트
⚠️ 아이콘 파일 - 프로젝트에 맞게 생성 필요

## 배포 전 체크리스트

- [ ] 모든 아이콘 파일 생성 및 배치
- [ ] Lighthouse PWA 점수 90점 이상
- [ ] 다양한 기기에서 설치 테스트
- [ ] 오프라인 모드 동작 확인
- [ ] 업데이트 알림 동작 확인
- [ ] 메타 태그 및 매니페스트 정보 검토
