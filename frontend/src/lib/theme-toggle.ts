function updateToggleIcons() {
  const isDark = document.documentElement.classList.contains('dark');
  const lightTip = document.documentElement.lang === 'cs' ? 'Přepnout na světlý režim' : 'Switch to light mode';
  const darkTip = document.documentElement.lang === 'cs' ? 'Přepnout na tmavý režim' : 'Switch to dark mode';
  const tip = isDark ? lightTip : darkTip;
  const btn = document.getElementById('theme-toggle');
  const btnM = document.getElementById('theme-toggle-mobile');
  if (btn) btn.title = tip;
  if (btnM) btnM.title = tip;
}

function toggleTheme() {
  document.documentElement.classList.add('no-transitions');
  const isDark = document.documentElement.classList.toggle('dark');
  localStorage.setItem('theme', isDark ? 'dark' : 'light');
  updateToggleIcons();
  // Force reflow so the no-transitions class takes effect before we remove it
  document.documentElement.offsetHeight;
  document.documentElement.classList.remove('no-transitions');
}

document.getElementById('theme-toggle')?.addEventListener('click', toggleTheme);
document.getElementById('theme-toggle-mobile')?.addEventListener('click', toggleTheme);
updateToggleIcons();
