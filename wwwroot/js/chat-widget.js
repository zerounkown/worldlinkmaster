// Site-wide support chat widget for signed-in customers. Connects to /hubs/chat (SignalR),
// starts/resumes the customer's open conversation, and streams messages (plus file/photo
// attachments, uploaded over plain HTTP) both ways.
(function () {
    "use strict";

    var widget = document.getElementById("chatWidget");
    if (!widget) {
        return;
    }

    var toggleBtn = document.getElementById("chatWidgetToggle");
    var closeBtn = document.getElementById("chatWidgetClose");
    var panel = document.getElementById("chatWidgetPanel");
    var messagesEl = document.getElementById("chatWidgetMessages");
    var form = document.getElementById("chatWidgetForm");
    var input = document.getElementById("chatWidgetInput");
    var attachBtn = document.getElementById("chatWidgetAttachBtn");
    var fileInput = document.getElementById("chatWidgetFile");

    var conversationId = null;
    var started = false;

    var connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/chat")
        .withAutomaticReconnect()
        .build();

    function escapeHtml(text) {
        var div = document.createElement("div");
        div.textContent = text;
        return div.innerHTML;
    }

    function formatFileSize(bytes) {
        if (!bytes && bytes !== 0) return "";
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return Math.round(bytes / 1024) + " KB";
        return (bytes / (1024 * 1024)).toFixed(1) + " MB";
    }

    function renderAttachment(m) {
        if (!m.attachmentUrl) {
            return "";
        }
        var downloadUrl = m.attachmentUrl + "?download=true";
        var fileName = escapeHtml(m.attachmentFileName || "attachment");
        var downloadBtn = '<a href="' + downloadUrl + '" download="' + fileName + '" class="chat-attachment-download" title="Download" aria-label="Download ' + fileName + '"><i class="bi bi-download"></i></a>';

        var viewLink = m.isImage
            ? '<a href="' + m.attachmentUrl + '" target="_blank" rel="noopener noreferrer" class="chat-attachment-image-link">' +
              '<img src="' + m.attachmentUrl + '" alt="' + fileName + '" class="chat-attachment-image" /></a>'
            : '<a href="' + m.attachmentUrl + '" target="_blank" rel="noopener noreferrer" class="chat-attachment-file">' +
              '<i class="bi bi-paperclip"></i> <span class="chat-attachment-file-name">' + fileName + '</span></a>';

        return '<div class="chat-attachment">' + viewLink + downloadBtn + '</div>';
    }

    function renderMessage(m) {
        var wrap = document.createElement("div");
        wrap.className = "chat-bubble " + (m.isFromSupport ? "chat-bubble-support" : "chat-bubble-me");
        var bodyHtml = m.body ? '<div>' + escapeHtml(m.body) + '</div>' : "";
        wrap.innerHTML =
            '<div class="chat-bubble-sender">' + escapeHtml(m.senderName || (m.isFromSupport ? "Support" : "You")) + '</div>' +
            renderAttachment(m) + bodyHtml;
        messagesEl.appendChild(wrap);
        messagesEl.scrollTop = messagesEl.scrollHeight;
    }

    connection.on("ReceiveMessage", function (convoId, message) {
        if (convoId === conversationId) {
            renderMessage(message);
        }
    });

    connection.on("ConversationClosed", function (convoId) {
        if (convoId === conversationId) {
            var notice = document.createElement("div");
            notice.className = "chat-bubble-notice";
            notice.textContent = "This conversation was closed by support.";
            messagesEl.appendChild(notice);
            messagesEl.scrollTop = messagesEl.scrollHeight;
        }
    });

    function ensureStarted() {
        if (started) {
            return Promise.resolve();
        }
        started = true;
        return connection.start()
            .then(function () { return connection.invoke("StartOrGetConversation"); })
            .then(function (id) {
                conversationId = id;
                return connection.invoke("GetHistory", id);
            })
            .then(function (history) {
                messagesEl.innerHTML = "";
                if (history.length === 0) {
                    var welcome = document.createElement("div");
                    welcome.className = "chat-bubble-notice";
                    welcome.textContent = "Hi! Send us a message and our team will get back to you.";
                    messagesEl.appendChild(welcome);
                } else {
                    history.forEach(renderMessage);
                }
            })
            .catch(function (err) {
                started = false;
                console.error(err);
            });
    }

    toggleBtn.addEventListener("click", function () {
        panel.hidden = !panel.hidden;
        if (!panel.hidden) {
            ensureStarted().then(function () { input.focus(); });
        }
    });

    closeBtn.addEventListener("click", function () {
        panel.hidden = true;
    });

    form.addEventListener("submit", function (e) {
        e.preventDefault();
        var text = input.value.trim();
        if (!text || conversationId === null) {
            return;
        }
        connection.invoke("SendMessage", conversationId, text).catch(function (err) { console.error(err); });
        input.value = "";
    });

    attachBtn.addEventListener("click", function () {
        ensureStarted().then(function () { fileInput.click(); });
    });

    fileInput.addEventListener("change", function () {
        var file = fileInput.files[0];
        if (!file || conversationId === null) {
            return;
        }

        var token = form.querySelector('input[name="__RequestVerificationToken"]').value;
        var formData = new FormData();
        formData.append("conversationId", conversationId);
        formData.append("file", file);
        formData.append("caption", input.value.trim());
        formData.append("__RequestVerificationToken", token);

        attachBtn.disabled = true;
        fetch("/chat/attachments", { method: "POST", body: formData })
            .then(function (resp) {
                if (!resp.ok) {
                    return resp.text().then(function (t) { throw new Error(t || "Upload failed."); });
                }
                input.value = "";
            })
            .catch(function (err) {
                alert(err.message || "Couldn't send that file.");
            })
            .finally(function () {
                attachBtn.disabled = false;
                fileInput.value = "";
            });
    });
})();
