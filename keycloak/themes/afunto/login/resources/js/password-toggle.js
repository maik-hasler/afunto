(function () {
	function togglePassword(button) {
		var inputId = button.getAttribute('aria-controls');
		var input = document.getElementById(inputId);
		if (!input) return;

		var isPassword = input.type === 'password';
		input.type = isPassword ? 'text' : 'password';

		var iconEl = button.querySelector('i');
		if (iconEl) {
			iconEl.className = isPassword
				? (button.dataset.iconHide || '')
				: (button.dataset.iconShow || '');
		}

		button.setAttribute('aria-label',
			isPassword
				? (button.dataset.labelHide || 'Hide password')
				: (button.dataset.labelShow || 'Show password'));
	}

	function init() {
		document.querySelectorAll('[data-password-toggle]').forEach(function (btn) {
			btn.addEventListener('click', function () { togglePassword(this); });
		});
	}

	if (document.readyState === 'loading') {
		document.addEventListener('DOMContentLoaded', init);
	} else {
		init();
	}
})();
