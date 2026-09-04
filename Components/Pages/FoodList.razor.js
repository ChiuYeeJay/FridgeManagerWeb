const key = 'fm-food-sort';

export function get() {
    return localStorage.getItem(key);
}

export function set(value) {
    localStorage.setItem(key, value);
}
