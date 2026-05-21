/* ============================================
   OTD System - App JavaScript Utilities
   Toast notifications, loading states, validation
   ============================================ */

(function () {
    'use strict';

    // ---------- Toast Notifications ----------
    window.Toast = {
        container: null,
        containers: {},

        getContainer(position = 'top-right') {
            if (this.containers[position]) return this.containers[position];

            const el = document.createElement('div');
            el.className = 'toast-container';
            if (position === 'bottom-right') el.classList.add('bottom-right');
            // default is top-right via CSS
            document.body.appendChild(el);
            this.containers[position] = el;
            return el;
        },

        show(type, title, message, duration = 5000, position = 'top-right') {
            const container = this.getContainer(position);

            const icons = {
                success: 'bi-check-circle-fill',
                danger: 'bi-exclamation-circle-fill',
                warning: 'bi-exclamation-triangle-fill',
                info: 'bi-info-circle-fill'
            };

            const toast = document.createElement('div');
            toast.className = `toast toast-${type}`;
            toast.innerHTML = `
                <i class="bi ${icons[type] || icons.info} toast-icon"></i>
                <div class="toast-content">
                    <div class="toast-title">${title}</div>
                    ${message ? `<div class="toast-message">${message}</div>` : ''}
                </div>
                <button class="toast-close" type="button">&times;</button>
            `;

            container.appendChild(toast);

            // Close button handler
            toast.querySelector('.toast-close').addEventListener('click', () => {
                this.dismiss(toast);
            });

            // Auto dismiss
            if (duration > 0) {
                setTimeout(() => this.dismiss(toast), duration);
            }

            return toast;
        },

        dismiss(toast) {
            toast.style.animation = 'slideOut 0.3s ease forwards';
            setTimeout(() => toast.remove(), 300);
        },

        success(title, message, duration) {
            return this.show('success', title, message, duration);
        },

        error(title, message, duration) {
            return this.show('danger', title, message, duration);
        },

        warning(title, message, duration) {
            return this.show('warning', title, message, duration);
        },

        info(title, message, duration) {
            return this.show('info', title, message, duration, 'top-right');
        }
    };

    // Convenience: show bottom-right info toast
    window.Toast.infoBottom = function (title, message, duration) {
        return window.Toast.show('info', title, message, duration, 'bottom-right');
    };

    // ---------- Loading States ----------
    window.Loading = {
        show(element) {
            if (!element) return;
            element.classList.add('loading');
            element.style.position = 'relative';
            
            const overlay = document.createElement('div');
            overlay.className = 'loading-overlay';
            overlay.innerHTML = '<div class="spinner spinner-lg"></div>';
            element.appendChild(overlay);
        },

        hide(element) {
            if (!element) return;
            element.classList.remove('loading');
            const overlay = element.querySelector('.loading-overlay');
            if (overlay) overlay.remove();
        },

        button(btn, loading = true) {
            if (!btn) return;
            if (loading) {
                btn.disabled = true;
                btn.dataset.originalText = btn.innerHTML;
                btn.innerHTML = '<div class="spinner"></div> Loading...';
            } else {
                btn.disabled = false;
                btn.innerHTML = btn.dataset.originalText || 'Submit';
            }
        }
    };

    // ---------- Form Validation ----------
    window.FormValidator = {
        validate(form) {
            let isValid = true;
            const inputs = form.querySelectorAll('[required], [data-validate]');

            inputs.forEach(input => {
                const valid = this.validateInput(input);
                if (!valid) isValid = false;
            });

            return isValid;
        },

        validateInput(input) {
            const value = input.value.trim();
            let isValid = true;
            let message = '';

            // Required check
            if (input.hasAttribute('required') && !value) {
                isValid = false;
                message = 'This field is required';
            }

            // Email validation
            if (isValid && input.type === 'email' && value) {
                const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
                if (!emailRegex.test(value)) {
                    isValid = false;
                    message = 'Please enter a valid email address';
                }
            }

            // Number validation
            if (isValid && input.type === 'number' && value) {
                const min = input.getAttribute('min');
                const max = input.getAttribute('max');
                const num = parseFloat(value);

                if (min !== null && num < parseFloat(min)) {
                    isValid = false;
                    message = `Value must be at least ${min}`;
                }
                if (max !== null && num > parseFloat(max)) {
                    isValid = false;
                    // Intentionally do not set a message for max violations to avoid the generic
                    // "Value must be at most X" text. Page-level or inline guidance (if present)
                    // should provide the user with more specific help.
                }
            }

            // Pattern validation
            if (isValid && input.pattern && value) {
                const regex = new RegExp(input.pattern);
                if (!regex.test(value)) {
                    isValid = false;
                    message = input.dataset.patternMessage || 'Invalid format';
                }
            }

            // Update UI
            this.setValidationState(input, isValid, message);

            return isValid;
        },

        setValidationState(input, isValid, message) {
            input.classList.remove('is-invalid', 'is-valid');

            let feedback = input.parentElement.querySelector('.invalid-feedback');

            if (!isValid) {
                input.classList.add('is-invalid');
                if (message) {
                    if (!feedback) {
                        feedback = document.createElement('div');
                        feedback.className = 'invalid-feedback';
                        input.parentElement.appendChild(feedback);
                    }
                    feedback.textContent = message;
                } else {
                    // No message to show; remove any existing feedback node so we don't render empty space
                    if (feedback) feedback.remove();
                }
            } else {
                if (feedback) feedback.remove();
                if (input.value.trim()) {
                    input.classList.add('is-valid');
                }
            }
        },

        clearValidation(form) {
            form.querySelectorAll('.is-invalid, .is-valid').forEach(el => {
                el.classList.remove('is-invalid', 'is-valid');
            });
            form.querySelectorAll('.invalid-feedback').forEach(el => el.remove());
        }
    };

    // ---------- Modal Helper ----------
    window.Modal = {
        show(id) {
            const modal = document.getElementById(id);
            if (modal) {
                modal.classList.add('show');
                document.body.style.overflow = 'hidden';
            }
        },

        hide(id) {
            const modal = document.getElementById(id);
            if (modal) {
                modal.classList.remove('show');
                document.body.style.overflow = '';
            }
        },

        toggle(id) {
            const modal = document.getElementById(id);
            if (modal) {
                if (modal.classList.contains('show')) {
                    this.hide(id);
                } else {
                    this.show(id);
                }
            }
        }
    };

    // ---------- Confirm Dialog ----------
    window.confirmAction = function(message, onConfirm, onCancel) {
        if (confirm(message)) {
            if (onConfirm) onConfirm();
            return true;
        } else {
            if (onCancel) onCancel();
            return false;
        }
    };

    // ---------- Initialize on DOM ready ----------
    document.addEventListener('DOMContentLoaded', function () {
        // Auto-dismiss alerts after 5 seconds
        document.querySelectorAll('.alert[data-auto-dismiss]').forEach(alert => {
            setTimeout(() => {
                alert.style.animation = 'slideOut 0.3s ease forwards';
                setTimeout(() => alert.remove(), 300);
            }, 5000);
        });

        // Form validation on submit
        document.querySelectorAll('form[data-validate]').forEach(form => {
            form.addEventListener('submit', function (e) {
                if (!FormValidator.validate(form)) {
                    e.preventDefault();
                    Toast.error('Validation Error', 'Please correct the errors in the form');
                }
            });
        });

        // Real-time validation on blur
        document.querySelectorAll('form[data-validate] input, form[data-validate] select').forEach(input => {
            input.addEventListener('blur', function () {
                FormValidator.validateInput(input);
            });
        });

        // Prevent double submit
        document.querySelectorAll('form').forEach(form => {
            form.addEventListener('submit', function () {
                const submitBtn = form.querySelector('button[type="submit"]');
                if (submitBtn && !submitBtn.disabled) {
                    Loading.button(submitBtn, true);
                }
            });
        });

        // Modal close on backdrop click
        document.querySelectorAll('.modal-backdrop').forEach(modal => {
            modal.addEventListener('click', function (e) {
                if (e.target === modal) {
                    Modal.hide(modal.id);
                }
            });
        });

        // Modal close on Escape key
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                document.querySelectorAll('.modal-backdrop.show').forEach(modal => {
                    Modal.hide(modal.id);
                });
            }
        });

        // Initialize tooltips (if Bootstrap is loaded)
        if (typeof bootstrap !== 'undefined' && bootstrap.Tooltip) {
            document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(el => {
                new bootstrap.Tooltip(el);
            });
        }

        // Global error hooks: surface syntax errors/unhandled rejections so developer and user can see them
        window.addEventListener('error', function (evt) {
            // avoid noisy errors from third-party libs where possible
            console.error('[GlobalError]', evt.message, evt.filename + ':' + evt.lineno + ':' + evt.colno);
            try { if (window.Toast) window.Toast.error('Client error', evt.message); } catch(e) {}
        });

        window.addEventListener('unhandledrejection', function (evt) {
            console.error('[UnhandledRejection]', evt.reason);
            try { if (window.Toast) window.Toast.error('Unhandled promise', (evt.reason && evt.reason.message) || String(evt.reason)); } catch(e) {}
        });

        // Detect accidental Login HTML on non-login pages (helps catch server-side redirect-to-login for XHR/static files)
        try {
            if (location && location.pathname && location.pathname.toLowerCase() !== '/login') {
                const looksLikeLogin = !!document.querySelector('form.login-form, input#username');
                if (looksLikeLogin) {
                    console.error('[GlobalError] Page contains Login HTML while at', location.pathname);
                    if (window.Toast) window.Toast.error('Session', 'This page appears to contain the login form — your session may have expired');
                }
            }
        } catch(e) { /* ignore in older browsers */ }

        // Quick check: validate that same-origin script resources return JavaScript (not HTML)
        (function validateLocalScripts(){
            try{
                const scripts = Array.from(document.querySelectorAll('script[src]'))
                    .map(s => s.getAttribute('src'))
                    .filter(u => u && u.startsWith('/') && !u.startsWith('//'));
                scripts.forEach(async (src) => {
                    try{
                        const resp = await fetch(src, { method: 'GET', cache: 'no-store' });
                        const ct = (resp.headers.get('content-type') || '').toLowerCase();
                        if(!ct || (!ct.includes('javascript') && !ct.includes('application/ecmascript'))) {
                            const txt = await resp.text();
                            console.error('[ResourceCheck] script', src, 'served unexpected content-type=', ct, 'snippet=', txt.slice(0,200));
                        }
                    }catch(err){ console.warn('[ResourceCheck] fetch failed for', src, err); }
                });
            }catch(e){ /* no-op */ }
        })();
    });

    // ---------- Utility Functions ----------
    window.Utils = {
        formatDate(dateString) {
            if (!dateString) return '';
            const date = new Date(dateString);
            return date.toLocaleDateString('en-GB', {
                day: '2-digit',
                month: '2-digit',
                year: 'numeric'
            });
        },

        formatDateTime(dateString) {
            if (!dateString) return '';
            const date = new Date(dateString);
            return date.toLocaleString('en-GB', {
                day: '2-digit',
                month: '2-digit',
                year: 'numeric',
                hour: '2-digit',
                minute: '2-digit'
            });
        },

        debounce(func, wait) {
            let timeout;
            return function executedFunction(...args) {
                const later = () => {
                    clearTimeout(timeout);
                    func(...args);
                };
                clearTimeout(timeout);
                timeout = setTimeout(later, wait);
            };
        },

        copyToClipboard(text) {
            navigator.clipboard.writeText(text).then(() => {
                Toast.success('Copied', 'Text copied to clipboard');
            }).catch(() => {
                Toast.error('Error', 'Failed to copy text');
            });
        }
    };

})();
