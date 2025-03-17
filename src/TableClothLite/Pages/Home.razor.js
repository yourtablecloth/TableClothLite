/**
 * 
 * @param {IntersectionObserverEntry[]} entries 
 */

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
