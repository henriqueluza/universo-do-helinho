'use strict';
// Tema guardado aplicado antes da primeira pintura: carregado sem defer no <head>, para quem escolheu
// o escuro não ver o claro piscar a cada página. A chave 'tema' e os valores são os do painel.js,
// que cuida do botão [data-tema] e da troca. Sem armazenamento (ou valor estranho), fica o claro.
try {
  const t = localStorage.getItem('tema');
  if (t === 'light' || t === 'dark') document.documentElement.dataset.theme = t;
} catch (_) { /* sem localStorage: fica o claro */ }
