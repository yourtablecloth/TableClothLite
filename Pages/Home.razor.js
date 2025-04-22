/**
 * 
 * @param {IntersectionObserverEntry[]} entries 
 */

// Chat.razor.js ���� ���� �Ǵ� ������Ʈ
window.adjustChatLayout = function () {
    // â ũ�Ⱑ ����� �� ä�� ���̾ƿ� ����
    function updateChatLayout() {
        const viewportHeight = window.innerHeight;
        const chatWrapper = document.querySelector('.chat-wrapper');

        if (chatWrapper) {
            // ����Ʈ ���̿� ���� �������� ���� ����
            // ���, �޽��� �����̳�, ���� ���
            const headerHeight = 64;
            const messageContainerHeight = 60;
            const margins = 16;

            // ����� ȯ�濡���� �ٸ� �� ����
            let adjustedHeight = viewportHeight - (headerHeight + messageContainerHeight + margins);

            if (window.innerWidth <= 768) {
                adjustedHeight = viewportHeight - (headerHeight + messageContainerHeight);
            }

            chatWrapper.style.height = `${adjustedHeight}px`;
        }
    }

    // �ʱ� �ε� �� ����
    updateChatLayout();

    // â ũ�� ���� �� ����
    window.addEventListener('resize', updateChatLayout);
};

function addHeadingObserverToAnchor(entries) {
    entries.forEach(({ target, isIntersecting }) => {
        if (!isIntersecting) {
            return;
        }
        const targetId = target.id;
        const targetElement = document.querySelector(`a[data-scroll-target-by][href="#${targetId}"]`);
        targetElement.ariaCurrent = "page";
        const notTargetElement = document.querySelectorAll(`a[data-scroll-target-by]:not([href="#${targetId}"])`);
        notTargetElement.forEach(element => {
            element.ariaCurrent = null;
        });
    })
}

const OBSERVER_OPTIONS = {
    rootMargin: ""
}

const observer = new IntersectionObserver(addHeadingObserverToAnchor, OBSERVER_OPTIONS);

export function initObserver() {
    const elements = document.querySelectorAll("[data-scroll-target]");
    console.log(elements);
    if (elements.length === 0) return false;
    elements.forEach(element => {
        observer.observe(element);
    });
    return true;
}
