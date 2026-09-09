(function () {
    'use strict';

    var LOG_PREFIX = '[JellyUnwatch]';
    var MARKED = 'jellyUnwatchReady';
    var MENU_ITEM_ID = 'jellyunwatch-hide';
    var lastCard = null;
    var lastCardAt = 0;
    var hiddenIds = {};
    var hiddenFetchedAt = 0;
    var hiddenFetchPending = false;
    var HIDDEN_TTL_MS = 15000;
    var options = null;
    var scheduled = null;
    var pressTimer = null;
    var longPressed = false;

    function log() {
        var args = Array.prototype.slice.call(arguments);
        args.unshift(LOG_PREFIX);
        console.log.apply(console, args);
    }

    function warn() {
        var args = Array.prototype.slice.call(arguments);
        args.unshift(LOG_PREFIX);
        console.warn.apply(console, args);
    }

    function getApiClient() {
        var client = window.ApiClient;
        if (!client || typeof client.getUrl !== 'function') {
            return null;
        }

        if (typeof client.accessToken === 'function' && !client.accessToken()) {
            return null;
        }

        return client;
    }

    function waitForApiClient(attempt) {
        var client = getApiClient();
        if (client) {
            start(client);
            return;
        }

        if (attempt > 120) {
            warn('ApiClient was not available, giving up');
            return;
        }

        setTimeout(function () {
            waitForApiClient(attempt + 1);
        }, 500);
    }

    function injectStyles() {
        if (document.getElementById('jelly-unwatch-styles')) {
            return;
        }

        var style = document.createElement('style');
        style.id = 'jelly-unwatch-styles';
        style.textContent = [
            '.jelly-unwatch-button { order: -1; }',
            '.jelly-unwatch-button.jelly-unwatch-busy { opacity: 0.5; pointer-events: none; }',
            '.jelly-unwatch-floating { position: absolute; top: 0.4em; right: 0.4em; z-index: 2;',
            'background: rgba(0, 0, 0, 0.6); border: 0; border-radius: 50%; color: #fff; padding: 0.35em; cursor: pointer; }',
            '.jelly-unwatch-floating .material-icons { font-size: 1.4em; }',
            '.jelly-unwatch-backdrop { position: fixed; inset: 0; z-index: 1000; display: flex; align-items: center;',
            'justify-content: center; background: rgba(0, 0, 0, 0.5); }',
            '.jelly-unwatch-dialog { max-width: 26em; margin: 1em; padding: 1.4em; border-radius: 0.4em;',
            'background: #242424; color: #fff; box-shadow: 0 0.2em 1.2em rgba(0, 0, 0, 0.6); }',
            '.jelly-unwatch-dialog h3 { margin: 0 0 0.6em 0; font-size: 1.2em; }',
            '.jelly-unwatch-dialog p { margin: 0 0 0.4em 0; }',
            '.jelly-unwatch-dialog .jelly-unwatch-secondary { opacity: 0.7; font-size: 0.9em; }',
            '.jelly-unwatch-dialog-buttons { display: flex; justify-content: flex-end; gap: 0.6em; margin-top: 1.2em; }',
            '.jelly-unwatch-dialog-buttons button { padding: 0.6em 1.2em; border: 0; border-radius: 0.25em;',
            'cursor: pointer; font-size: 1em; }',
            '.jelly-unwatch-confirm { background: #00a4dc; color: #fff; }',
            '.jelly-unwatch-cancel { background: rgba(255, 255, 255, 0.15); color: #fff; }',
            '.jelly-unwatch-reset { display: flex; align-items: center; gap: 0.5em; margin-top: 1em; cursor: pointer; }',
            '.jelly-unwatch-reset input { width: 1.1em; height: 1.1em; cursor: pointer; }'
        ].join('\n');
        document.head.appendChild(style);
    }

    function isTrackedCard(card) {
        var container = card.closest('.itemsContainer[data-monitor]');
        return !!container && isResumeContainer(container);
    }

    function trackCard(event) {
        var target = event.target;
        var card = target && target.closest ? target.closest('.card[data-id]') : null;

        if (!card) {
            return;
        }

        lastCard = isTrackedCard(card) ? card : null;
        lastCardAt = Date.now();
    }

    function isResumeContainer(container) {
        var monitor = container.getAttribute('data-monitor') || '';
        return monitor.indexOf('playback') !== -1;
    }

    function findCards() {
        var containers = document.querySelectorAll('.itemsContainer[data-monitor]');
        var cards = [];

        for (var i = 0; i < containers.length; i++) {
            if (!isResumeContainer(containers[i])) {
                continue;
            }

            var found = containers[i].querySelectorAll('.card[data-id]');
            for (var j = 0; j < found.length; j++) {
                cards.push(found[j]);
            }
        }

        return cards;
    }

    function createButton(floating, label) {
        var button = document.createElement('button');
        button.type = 'button';
        button.className = floating
            ? 'jelly-unwatch-button jelly-unwatch-floating'
            : 'jelly-unwatch-button cardOverlayButton cardOverlayButton-hover paper-icon-button-light';
        button.title = label;
        button.setAttribute('aria-label', label);
        button.innerHTML = floating
            ? '<span class="material-icons close" aria-hidden="true"></span>'
            : '<span class="material-icons cardOverlayButtonIcon cardOverlayButtonIcon-hover close" aria-hidden="true"></span>';
        return button;
    }

    function findButtonTarget(card) {
        var overlayButtons = card.querySelector('.cardOverlayContainer .cardOverlayButton-br');
        if (overlayButtons) {
            return { element: overlayButtons, floating: false };
        }

        var scalable = card.querySelector('.cardScalable');
        if (scalable) {
            return { element: scalable, floating: true };
        }

        return null;
    }

    function getSection(card) {
        return card.closest('.verticalSection') || card.closest('.homeSection');
    }

    function isNextUpCard(card) {
        var section = getSection(card);
        return !!(section && section.querySelector('a[href*="nextup" i]'));
    }

    function rowLabel(card) {
        return isNextUpCard(card) ? 'Next Up' : 'Continue Watching';
    }

    function normalizeId(itemId) {
        return String(itemId).replace(/-/g, '').toLowerCase();
    }

    function suppress(itemId) {
        hiddenIds[normalizeId(itemId)] = true;
    }

    function isSuppressed(itemId) {
        return hiddenIds[normalizeId(itemId)] === true;
    }

    function loadHiddenIds(client) {
        if (hiddenFetchPending || Date.now() - hiddenFetchedAt < HIDDEN_TTL_MS) {
            return;
        }

        hiddenFetchPending = true;

        client.getJSON(client.getUrl('JellyUnwatch/Items')).then(function (items) {
            var next = {};

            for (var i = 0; i < items.length; i++) {
                next[normalizeId(items[i].ItemId)] = true;
            }

            hiddenIds = next;
            hiddenFetchedAt = Date.now();
            hiddenFetchPending = false;
        }).catch(function (error) {
            hiddenFetchedAt = Date.now();
            hiddenFetchPending = false;
            warn('Failed to load the hidden list', error);
        });
    }

    function refreshContainer(container) {
        if (!container || typeof container.refreshItems !== 'function') {
            return;
        }

        try {
            container.refreshItems();
        } catch (error) {
            warn('Failed to refresh the row', error);
        }
    }

    function hideEmptySection(container, section) {
        if (!container || !section || container.querySelector('.card[data-id]')) {
            return;
        }

        section.classList.add('hide');
    }

    function getCardTitle(card) {
        var label = card.getAttribute('aria-label');
        if (label) {
            return label;
        }

        var text = card.querySelector('.cardText');
        return text ? text.textContent.trim() : 'this item';
    }

    function showConfirmDialog(message, note, allowReset, resetChecked) {
        return new Promise(function (resolve) {
            var backdrop = document.createElement('div');
            backdrop.className = 'jelly-unwatch-backdrop';

            var dialog = document.createElement('div');
            dialog.className = 'jelly-unwatch-dialog';
            dialog.setAttribute('role', 'dialog');
            dialog.innerHTML = '<h3>JellyUnwatch</h3><p class="jelly-unwatch-message"></p>'
                + '<p class="jelly-unwatch-secondary"></p>'
                + (allowReset
                    ? '<label class="jelly-unwatch-reset"><input type="checkbox" class="jelly-unwatch-reset-input"/>'
                        + '<span>Also reset playback progress</span></label>'
                    : '')
                + '<div class="jelly-unwatch-dialog-buttons">'
                + '<button type="button" class="jelly-unwatch-cancel">Cancel</button>'
                + '<button type="button" class="jelly-unwatch-confirm">Remove</button>'
                + '</div>';

            dialog.querySelector('.jelly-unwatch-message').textContent = message;
            dialog.querySelector('.jelly-unwatch-secondary').textContent = note;

            var resetInput = dialog.querySelector('.jelly-unwatch-reset-input');
            if (resetInput) {
                resetInput.checked = !!resetChecked;
            }

            var closed = false;

            function close(accepted) {
                if (closed) {
                    return;
                }

                closed = true;
                document.removeEventListener('keydown', onKey, true);

                var clearResume = resetInput ? resetInput.checked : null;

                if (backdrop.parentNode) {
                    backdrop.parentNode.removeChild(backdrop);
                }

                resolve(accepted ? { clearResume: clearResume } : null);
            }

            function onKey(event) {
                if (event.key === 'Escape') {
                    event.stopPropagation();
                    close(false);
                } else if (event.key === 'Enter') {
                    event.stopPropagation();
                    close(true);
                }
            }

            dialog.querySelector('.jelly-unwatch-cancel').addEventListener('click', function () {
                close(false);
            });

            dialog.querySelector('.jelly-unwatch-confirm').addEventListener('click', function () {
                close(true);
            });

            backdrop.addEventListener('click', function (event) {
                if (event.target === backdrop) {
                    close(false);
                }
            });

            document.addEventListener('keydown', onKey, true);

            backdrop.appendChild(dialog);
            document.body.appendChild(backdrop);
            dialog.querySelector('.jelly-unwatch-confirm').focus();
        });
    }

    function confirmHide(card, wholeSeries) {
        if (!options || !options.ConfirmBeforeHiding) {
            return Promise.resolve({ clearResume: null });
        }

        var title = getCardTitle(card);
        var nextUp = isNextUpCard(card);
        var message = wholeSeries
            ? 'Remove the whole show of "' + title + '" from Continue Watching and Next Up?'
            : 'Remove "' + title + '" from ' + rowLabel(card) + '?';
        var allowReset = !!options.AllowResetPlaybackProgress && !nextUp;
        var note = allowReset
            ? 'Playing it again restores the entry. The playback position is kept unless you check the box.'
            : 'Playing it again restores the entry.';

        return showConfirmDialog(message, note, allowReset, options.ClearResumePositionOnHide);
    }

    function hideItem(client, card, button, wholeSeries) {
        var itemId = card.getAttribute('data-id');
        if (!itemId) {
            return;
        }

        confirmHide(card, wholeSeries).then(function (result) {
            if (result) {
                performHide(client, card, button, wholeSeries, itemId, result.clearResume);
            }
        });
    }

    function performHide(client, card, button, wholeSeries, itemId, clearResume) {
        var container = card.closest('.itemsContainer');
        var section = getSection(card);
        button.classList.add('jelly-unwatch-busy');

        var query = { scope: wholeSeries ? 'Series' : 'Item' };

        if (clearResume === true || clearResume === false) {
            query.clearResume = clearResume;
        }

        client.ajax({
            type: 'POST',
            url: client.getUrl('JellyUnwatch/Items/' + itemId, query)
        }).then(function () {
            suppress(itemId);

            if (card.parentNode) {
                card.parentNode.removeChild(card);
            }

            hideEmptySection(container, section);
            refreshContainer(container);
            log('Hid item', itemId, wholeSeries ? '(series)' : '(item)');
        }).catch(function (error) {
            button.classList.remove('jelly-unwatch-busy');
            warn('Failed to hide item', itemId, error);
        });
    }

    function attachButton(client, card) {
        if (card.dataset[MARKED]) {
            return;
        }

        var target = findButtonTarget(card);
        if (!target) {
            return;
        }

        card.dataset[MARKED] = '1';

        var button = createButton(target.floating, 'Remove from ' + rowLabel(card));

        button.addEventListener('click', function (event) {
            event.preventDefault();
            event.stopPropagation();

            var wholeSeries = event.shiftKey || longPressed;
            longPressed = false;
            hideItem(client, card, button, wholeSeries);
        });

        button.addEventListener('touchstart', function () {
            longPressed = false;
            pressTimer = setTimeout(function () {
                longPressed = true;
            }, 600);
        }, { passive: true });

        button.addEventListener('touchend', function () {
            clearTimeout(pressTimer);
        }, { passive: true });

        if (target.floating) {
            target.element.appendChild(button);
        } else {
            target.element.insertBefore(button, target.element.firstChild);
        }
    }

    function injectMenuItem(client, dialog) {
        if (!dialog || dialog.querySelector('.jelly-unwatch-menu-item')) {
            return;
        }

        if (!lastCard || !document.body.contains(lastCard) || Date.now() - lastCardAt > 5000) {
            return;
        }

        var scroller = dialog.querySelector('.actionSheetScroller');
        if (!scroller) {
            return;
        }

        var card = lastCard;
        var label = 'Remove from ' + rowLabel(card);
        var button = document.createElement('button');
        button.setAttribute('is', 'emby-button');
        button.type = 'button';
        button.className = 'listItem listItem-button actionSheetMenuItem jelly-unwatch-menu-item';
        button.setAttribute('data-id', MENU_ITEM_ID);
        button.innerHTML = '<span class="actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons close" aria-hidden="true"></span>'
            + '<div class="listItemBody actionsheetListItemBody">'
            + '<div class="listItemBodyText actionSheetItemText">' + label + '</div>'
            + '</div>';

        button.addEventListener('click', function (event) {
            hideItem(client, card, button, event.shiftKey);
        }, true);

        var divider = scroller.querySelector('.actionsheetDivider');
        if (divider) {
            scroller.insertBefore(button, divider);
        } else {
            scroller.appendChild(button);
        }
    }

    function injectMenuItems(client, node) {
        if (node.classList && node.classList.contains('actionSheet')) {
            injectMenuItem(client, node);
            return;
        }

        if (node.querySelectorAll) {
            var sheets = node.querySelectorAll('.actionSheet');
            for (var i = 0; i < sheets.length; i++) {
                injectMenuItem(client, sheets[i]);
            }
        }
    }

    function refresh(client) {
        loadHiddenIds(client);

        var cards = findCards();

        for (var i = 0; i < cards.length; i++) {
            var card = cards[i];
            var itemId = card.getAttribute('data-id');

            if (itemId && isSuppressed(itemId)) {
                var container = card.closest('.itemsContainer');
                var section = getSection(card);

                if (card.parentNode) {
                    card.parentNode.removeChild(card);
                }

                hideEmptySection(container, section);
                log('Removed a re-rendered card for hidden item', itemId);
                continue;
            }

            attachButton(client, card);
        }
    }

    function scheduleRefresh(client) {
        if (scheduled) {
            return;
        }

        scheduled = setTimeout(function () {
            scheduled = null;
            try {
                refresh(client);
            } catch (error) {
                warn('Failed to attach buttons', error);
            }
        }, 150);
    }

    function observe(client) {
        var observer = new MutationObserver(function (mutations) {
            for (var i = 0; i < mutations.length; i++) {
                var added = mutations[i].addedNodes;

                for (var j = 0; j < added.length; j++) {
                    if (added[j].nodeType === 1) {
                        injectMenuItems(client, added[j]);
                    }
                }
            }

            scheduleRefresh(client);
        });

        document.addEventListener('click', trackCard, true);
        document.addEventListener('contextmenu', trackCard, true);
        document.addEventListener('touchstart', trackCard, { capture: true, passive: true });

        observer.observe(document.body, { childList: true, subtree: true });
        scheduleRefresh(client);
    }

    function start(client) {
        client.getJSON(client.getUrl('JellyUnwatch/ClientOptions')).then(function (result) {
            options = result;
            loadHiddenIds(client);

            if (!options.EnableWebButton) {
                log('The web button is disabled in the plugin configuration');
                return;
            }

            injectStyles();
            observe(client);
            log('Ready');
        }).catch(function (error) {
            warn('Failed to load client options', error);
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () {
            waitForApiClient(0);
        });
    } else {
        waitForApiClient(0);
    }
})();
