export function scrollToTop() {
    window.scrollTo({ top: 0, behavior: "smooth" });
}

export function scrollToInvalid() {
    const root = document.querySelector(".form-grid");
    if (!root) {
        return;
    }

    const target = root.querySelector(".invalid, .fm-input.is-invalid, .validation-message");
    target?.scrollIntoView({ behavior: "smooth", block: "center" });
}
