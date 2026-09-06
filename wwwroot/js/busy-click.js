document.addEventListener("click", (event) => {
    const target = event.target;
    if (!(target instanceof Element)) {
        return;
    }

    const button = target.closest("[data-fm-busy-on-click]");
    if (!(button instanceof HTMLButtonElement)) {
        return;
    }

    markBusy(button);
}, true);

document.addEventListener("submit", (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement)) {
        return;
    }

    const submitted = event.submitter instanceof HTMLButtonElement
        ? event.submitter
        : form.querySelector("button[type='submit'][data-fm-busy-on-click]");
    if (!(submitted instanceof HTMLButtonElement) || !submitted.hasAttribute("data-fm-busy-on-click")) {
        return;
    }

    markBusy(submitted);
}, true);

function markBusy(button) {
    if (button.disabled || button.dataset.fmBusy === "true") {
        return;
    }

    button.dataset.fmBusy = "true";
    button.classList.add("is-pending");
    const label = button.getAttribute("data-fm-busy-label");
    if (label) {
        button.textContent = label;
    }
}
