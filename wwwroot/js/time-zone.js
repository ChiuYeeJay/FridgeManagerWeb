export function get() {
    return Intl.DateTimeFormat().resolvedOptions().timeZone;
}
