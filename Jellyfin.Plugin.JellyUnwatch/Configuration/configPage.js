const pluginId = '0340db8a-f7ee-4446-9661-051a80b16b67';

function loadConfiguration(view) {
    return ApiClient.getPluginConfiguration(pluginId).then(function (config) {
        view.querySelector('#EnableWebButton').checked = config.EnableWebButton;
        view.querySelector('#HideSeriesFromNextUp').checked = config.HideSeriesFromNextUp;
        view.querySelector('#ClearResumePositionOnHide').checked = config.ClearResumePositionOnHide;
        view.querySelector('#AllowResetPlaybackProgress').checked = config.AllowResetPlaybackProgress;
        view.querySelector('#AutoUnhideOnPlayback').checked = config.AutoUnhideOnPlayback;
        view.querySelector('#ConfirmBeforeHiding').checked = config.ConfirmBeforeHiding;
    });
}

function escapeHtml(value) {
    const element = document.createElement('span');
    element.textContent = value == null ? '' : value;
    return element.innerHTML;
}

function renderHiddenItems(view, users) {
    const target = view.querySelector('#JellyUnwatchHiddenItems');

    if (!users.length) {
        target.innerHTML = '<p class="fieldDescription">Nothing is hidden right now.</p>';
        return;
    }

    let html = '';

    users.forEach(function (user) {
        html += '<div class="verticalSection">';
        html += '<h3>' + escapeHtml(user.UserName) + '</h3>';
        html += '<div class="paperList">';

        user.Items.forEach(function (item) {
            const hiddenAt = new Date(item.HiddenAtUtc).toLocaleString();
            const scope = item.Scope === 1 || item.Scope === 'Series' ? 'whole series' : 'single item';

            html += '<div class="listItem listItem-border">';
            html += '<div class="listItemBody">';
            html += '<div class="listItemBodyText">' + escapeHtml(item.Name || item.ItemId) + '</div>';
            html += '<div class="listItemBodyText secondary">' + scope + ', hidden ' + escapeHtml(hiddenAt) + '</div>';
            html += '</div>';
            html += '<button is="paper-icon-button-light" type="button" class="btnRestore" ' +
                'data-userid="' + escapeHtml(user.UserId) + '" data-itemid="' + escapeHtml(item.ItemId) + '" title="Restore">' +
                '<span class="material-icons undo" aria-hidden="true"></span></button>';
            html += '</div>';
        });

        html += '</div></div>';
    });

    target.innerHTML = html;

    target.querySelectorAll('.btnRestore').forEach(function (button) {
        button.addEventListener('click', function () {
            const url = ApiClient.getUrl('JellyUnwatch/Users/' + button.dataset.userid + '/Items/' + button.dataset.itemid);
            ApiClient.ajax({ type: 'DELETE', url: url }).then(function () {
                loadHiddenItems(view);
            });
        });
    });
}

function loadHiddenItems(view) {
    return ApiClient.getJSON(ApiClient.getUrl('JellyUnwatch/Users')).then(function (users) {
        renderHiddenItems(view, users);
    }).catch(function () {
        view.querySelector('#JellyUnwatchHiddenItems').innerHTML =
            '<p class="fieldDescription">The hidden item list could not be loaded.</p>';
    });
}

function loadStatus(view) {
    return ApiClient.getJSON(ApiClient.getUrl('JellyUnwatch/ClientOptions')).then(function () {
        view.querySelector('#JellyUnwatchStatus').textContent =
            'The plugin is active. Hidden items are filtered out of Continue Watching and Next Up for every client.';
    }).catch(function () {
        view.querySelector('#JellyUnwatchStatus').textContent = 'The plugin API is not responding.';
    });
}

export default function (view) {
    view.addEventListener('viewshow', function () {
        Dashboard.showLoadingMsg();

        Promise.all([
            loadConfiguration(view),
            loadHiddenItems(view),
            loadStatus(view)
        ]).then(function () {
            Dashboard.hideLoadingMsg();
        }).catch(function () {
            Dashboard.hideLoadingMsg();
        });
    });

    view.querySelector('#JellyUnwatchConfigForm').addEventListener('submit', function (event) {
        event.preventDefault();
        Dashboard.showLoadingMsg();

        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            config.EnableWebButton = view.querySelector('#EnableWebButton').checked;
            config.HideSeriesFromNextUp = view.querySelector('#HideSeriesFromNextUp').checked;
            config.ClearResumePositionOnHide = view.querySelector('#ClearResumePositionOnHide').checked;
            config.AllowResetPlaybackProgress = view.querySelector('#AllowResetPlaybackProgress').checked;
            config.AutoUnhideOnPlayback = view.querySelector('#AutoUnhideOnPlayback').checked;
            config.ConfirmBeforeHiding = view.querySelector('#ConfirmBeforeHiding').checked;

            ApiClient.updatePluginConfiguration(pluginId, config).then(function (result) {
                Dashboard.processPluginConfigurationUpdateResult(result);
            });
        });

        return false;
    });
}
