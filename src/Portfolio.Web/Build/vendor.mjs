import { copyFileSync, mkdirSync } from 'node:fs';
mkdirSync('wwwroot/js/vendor', { recursive: true });
copyFileSync('node_modules/sortablejs/modular/sortable.complete.esm.js', 'wwwroot/js/vendor/sortable.esm.js');
