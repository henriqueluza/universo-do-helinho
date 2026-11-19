'use strict';
// Aplica o tema salvo antes da primeira pintura (script bloqueante no <head>, sem inline).
// Mesma chave e mesmos valores do módulo de tema do painel.js. Sem preferência salva, vale o claro (como a preview).
try {
  const t = localStorage.getItem('tema');
  if (t === 'light' || t === 'dark') document.documentElement.dataset.theme = t;
} catch (_) { /* sem armazenamento: fica o tema claro */ }
