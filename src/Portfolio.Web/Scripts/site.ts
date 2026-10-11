import Sortable from './vendor/sortable.esm.js';
const theme = document.documentElement;
try { const saved = localStorage.getItem('portfolio-theme'); if (saved === 'light' || saved === 'dark') theme.dataset.theme = saved; } catch { /* browser storage optional */ }
document.querySelectorAll<HTMLButtonElement>('.theme-toggle').forEach(button => button.addEventListener('click', () => { const next = theme.dataset.theme === 'light' ? 'dark' : 'light'; theme.dataset.theme = next; try { localStorage.setItem('portfolio-theme', next); } catch { /* optional preference */ } }));
const menu = document.querySelector<HTMLButtonElement>('.mobile-toggle');
menu?.addEventListener('click', () => { const expanded = menu.getAttribute('aria-expanded') !== 'true'; menu.setAttribute('aria-expanded', String(expanded)); document.getElementById('main-nav')?.classList.toggle('open', expanded); });
document.querySelectorAll<HTMLAnchorElement>('.nav-links a, .admin-sidebar nav a').forEach(link => { if (new URL(link.href).pathname === location.pathname) link.setAttribute('aria-current', 'page'); });
function status(message: string) { const element = document.getElementById('order-status'); if (element) element.textContent = message; }
for (const panel of document.querySelectorAll<HTMLElement>('.order-panel')) {
    const list = panel.querySelector<HTMLElement>('[data-sortable]'); if (!list) continue;
    const disabled = panel.dataset.disabled === 'true';
    if (disabled) panel.querySelectorAll<HTMLButtonElement>('.move-up,.move-down').forEach(b => b.disabled = true);
    else {
        new Sortable(list, { handle: '.drag-handle', animation: matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 150, ghostClass: 'sortable-ghost' });
        list.addEventListener('click', event => {
            const button = (event.target as Element).closest<HTMLButtonElement>('.move-up,.move-down');
            const row = button?.closest<HTMLElement>('[data-id]'); if (!button || !row) return;
            if (button.classList.contains('move-up') && row.previousElementSibling) list.insertBefore(row, row.previousElementSibling);
            if (button.classList.contains('move-down') && row.nextElementSibling) list.insertBefore(row.nextElementSibling, row);
            button.focus(); status('Order updated locally. Save to persist it.');
        });
        panel.querySelector<HTMLButtonElement>('.save-order')?.addEventListener('click', async event => {
            const button = event.currentTarget as HTMLButtonElement; button.disabled = true; button.textContent = 'Saving…';
            try {
                const response = await fetch(panel.dataset.endpoint!, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': document.querySelector<HTMLMetaElement>('meta[name=csrf-token]')!.content }, body: JSON.stringify({ ids: Array.from(list.children).map(row => (row as HTMLElement).dataset.id), version: Number(panel.dataset.version) }) });
                if (response.redirected) throw new Error('Your session expired. Sign in again before saving.');
                const data = await response.json() as { version: number; ids: string[]; detail?: string };
                if (!response.ok) throw new Error(data.detail ?? 'Could not save. Refresh and retry.');
                panel.dataset.version = String(data.version);
                for (const id of data.ids) { const row = Array.from(list.children).find(x => (x as HTMLElement).dataset.id === id); if (row) list.append(row); }
                status('Order saved successfully.');
            } catch (error) { status(error instanceof Error ? error.message : 'Unable to save. Please retry.'); }
            finally { button.disabled = false; button.textContent = 'Save order'; }
        });
    }
}
for (const area of document.querySelectorAll<HTMLTextAreaElement>('[data-array-name]')) {
    const update = () => { const name = area.dataset.arrayName!; const container = area.parentElement!.querySelector<HTMLElement>(`[data-array-values="${name}"]`)!; container.replaceChildren(); area.value.split('\n').map(x => x.trim()).filter(Boolean).forEach((value, index) => { const input = document.createElement('input'); input.type = 'hidden'; input.name = `${name}[${index}]`; input.value = value; container.append(input); }); };
    area.addEventListener('input', update); area.form?.addEventListener('submit', update);
}
const dialog = document.querySelector<HTMLDialogElement>('#confirm-dialog');
let pendingForm: HTMLFormElement | null = null;
document.querySelectorAll<HTMLFormElement>('form[data-confirm]').forEach(form => form.addEventListener('submit', event => { if (dialog && !form.dataset.confirmed) { event.preventDefault(); pendingForm = form; document.getElementById('confirm-message')!.textContent = form.dataset.confirm!; dialog.showModal(); } }));
document.getElementById('confirm-cancel')?.addEventListener('click', () => { dialog?.close(); pendingForm = null; });
document.getElementById('confirm-ok')?.addEventListener('click', () => { dialog?.close(); if (pendingForm) { pendingForm.dataset.confirmed = 'true'; pendingForm.requestSubmit(); } });
const fileInput = document.getElementById('files') as HTMLInputElement | null;
fileInput?.addEventListener('change', () => { const container = document.getElementById('upload-previews')!; container.querySelectorAll<HTMLImageElement>('img').forEach(img => URL.revokeObjectURL(img.src)); container.replaceChildren(); for (const file of Array.from(fileInput.files ?? []).slice(0, 4)) { const item = document.createElement('div'); const label = document.createElement('p'); label.textContent = file.name; item.append(label); if (file.type.startsWith('image/') && file.type !== 'image/svg+xml') { const img = document.createElement('img'); img.src = URL.createObjectURL(file); img.alt = 'Selected upload preview'; item.append(img); } container.append(item); } });
