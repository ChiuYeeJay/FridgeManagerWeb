document.addEventListener("click", (event) => {
    const target = event.target;
    if (!(target instanceof Element)) {
        return;
    }

    const button = target.closest("[data-password-toggle]");
    if (!(button instanceof HTMLElement)) {
        return;
    }

    const input = button.closest(".fm-password")?.querySelector("input");
    if (!(input instanceof HTMLInputElement)) {
        return;
    }

    const reveal = input.type === "password";
    input.type = reveal ? "text" : "password";
    button.setAttribute("aria-pressed", reveal ? "true" : "false");
    button.setAttribute("aria-label", reveal ? "Hide password" : "Show password");
});
