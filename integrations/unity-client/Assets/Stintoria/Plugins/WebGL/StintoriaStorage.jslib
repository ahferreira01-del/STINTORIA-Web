mergeInto(LibraryManager.library, {
  $StintoriaBrowser: {
    errorCode: function (e) {
      return e && e.name === "QuotaExceededError" ? "quota" : "unavailable";
    },
    operation: function (receiver, id, slot, write) {
      var done = false, db = null, tx = null, failure = null;
      function finish(ok, value) {
        if (done) return;
        done = true; clearTimeout(timer);
        if (db) db.close();
        SendMessage(receiver, "OnStorageReply", id + "|" + (ok ? "ok" : "err") + "|" + value);
      }
      var timer = setTimeout(function () {
        if (tx) { try { tx.abort(); } catch (_) {} }
        finish(false, write ? "uncertain" : "unavailable");
      }, 15000);
      try {
        if (slot < 1 || slot > 3 || !globalThis.indexedDB) throw new Error("unavailable");
        var opening = indexedDB.open("STINTORIA-Kart-offline", 1);
        opening.onupgradeneeded = function () {
          if (!opening.result.objectStoreNames.contains("banks")) opening.result.createObjectStore("banks");
        };
        opening.onerror = function () { finish(false, StintoriaBrowser.errorCode(opening.error)); };
        opening.onblocked = function () { finish(false, "unavailable"); };
        opening.onsuccess = function () {
          db = opening.result;
          if (done) { db.close(); return; }
          db.onversionchange = function () { db.close(); };
          try {
            tx = db.transaction("banks", write ? "readwrite" : "readonly");
            var store = tx.objectStore("banks"), a, b, received = 0;
            var keyA = "slot-" + slot + ":A", keyB = "slot-" + slot + ":B";
            function accept() {
              if (++received !== 2 || !write) return;
              if (a !== write.expectedA || b !== write.expectedB) {
                failure = "conflict"; tx.abort(); return;
              }
              // onsuccess de put NÃO confirma persistência. Só tx.oncomplete responde ok.
              try { store.put(write.record, write.bankA ? keyA : keyB); }
              catch (e) { failure = StintoriaBrowser.errorCode(e); tx.abort(); }
            }
            var getA = store.get(keyA), getB = store.get(keyB);
            getA.onsuccess = function () { a = getA.result === undefined ? "" : getA.result; accept(); };
            getB.onsuccess = function () { b = getB.result === undefined ? "" : getB.result; accept(); };
            tx.oncomplete = function () { finish(true, write ? "" : encodeURIComponent(a) + ";" + encodeURIComponent(b)); };
            tx.onabort = function () { finish(false, failure || StintoriaBrowser.errorCode(tx.error)); };
            tx.onerror = function (event) {
              failure = failure || StintoriaBrowser.errorCode((event && event.target && event.target.error) || tx.error);
            };
          } catch (e) { finish(false, StintoriaBrowser.errorCode(e)); }
        };
      } catch (e) { finish(false, StintoriaBrowser.errorCode(e)); }
    }
  },
  StintoriaStorage_Read__deps: ["$StintoriaBrowser"],
  StintoriaStorage_Read: function (receiver, id, slot) {
    StintoriaBrowser.operation(UTF8ToString(receiver), id, slot, null);
  },
  StintoriaStorage_Write__deps: ["$StintoriaBrowser"],
  StintoriaStorage_Write: function (receiver, id, slot, bankA, record, expectedA, expectedB) {
    StintoriaBrowser.operation(UTF8ToString(receiver), id, slot, {
      bankA: !!bankA, record: UTF8ToString(record),
      expectedA: UTF8ToString(expectedA), expectedB: UTF8ToString(expectedB)
    });
  },
  StintoriaStorage_Download: function (receiver, id, checkpoint) {
    var name = UTF8ToString(receiver);
    try {
      var raw = atob(UTF8ToString(checkpoint)), bytes = new Uint8Array(raw.length);
      for (var i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
      var url = URL.createObjectURL(new Blob([bytes], {type: "application/octet-stream"}));
      var link = document.createElement("a"); link.href = url; link.download = "STINTORIA-carreira.stint";
      document.body.appendChild(link); link.click(); link.remove();
      setTimeout(function () { URL.revokeObjectURL(url); }, 60000);
      // Só confirma oferta ao navegador, NÃO a conclusão de um download no disco.
      SendMessage(name, "OnStorageReply", id + "|ok|");
    } catch (_) { SendMessage(name, "OnStorageReply", id + "|err|unavailable"); }
  },
  StintoriaStorage_Choose: function (receiver, id) {
    var name = UTF8ToString(receiver), input = document.createElement("input"), done = false;
    function finish(ok, value) {
      if (done) return; done = true; clearTimeout(timer); input.remove();
      SendMessage(name, "OnStorageReply", id + "|" + (ok ? "ok" : "err") + "|" + value);
    }
    var timer = setTimeout(function () { finish(false, "cancelled"); }, 120000);
    input.type = "file"; input.accept = ".stint"; input.style.display = "none";
    input.oncancel = function () { finish(false, "cancelled"); };
    input.onchange = function () {
      var file = input.files && input.files[0];
      if (!file) { finish(false, "cancelled"); return; }
      if (file.size > 65536) { finish(false, "unavailable"); return; }
      var reader = new FileReader();
      reader.onerror = function () { finish(false, "unavailable"); };
      reader.onload = function () {
        var bytes = new Uint8Array(reader.result), raw = "";
        for (var i = 0; i < bytes.length; i++) raw += String.fromCharCode(bytes[i]);
        finish(true, btoa(raw));
      };
      reader.readAsArrayBuffer(file);
    };
    document.body.appendChild(input);
    try { input.click(); } catch (_) { finish(false, "unavailable"); }
  }
});
