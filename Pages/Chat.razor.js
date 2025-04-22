﻿// Chat.razor.js 파일 생성 또는 업데이트
window.adjustChatLayout = function () {
    // 창 크기가 변경될 때 채팅 레이아웃 조절
    function updateChatLayout() {
        const viewportHeight = window.innerHeight;
        const chatWrapper = document.querySelector('.chat-wrapper');

        if (chatWrapper) {
            // 뷰포트 높이에 따라 동적으로 높이 조정
            // 헤더, 메시지 컨테이너, 여백 고려
            const headerHeight = 64;
            const messageContainerHeight = 60;
            const margins = 16;

            // 모바일 환경에서는 다른 값 적용
            let adjustedHeight = viewportHeight - (headerHeight + messageContainerHeight + margins);

            if (window.innerWidth <= 768) {
                adjustedHeight = viewportHeight - (headerHeight + messageContainerHeight);
            }

            chatWrapper.style.height = `${adjustedHeight}px`;
        }
    }

    // 초기 로드 시 실행
    updateChatLayout();

    // 창 크기 변경 시 실행
    window.addEventListener('resize', updateChatLayout);
};
