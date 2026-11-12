// The web build's downloads: a fight's telemetry handed to the browser as a file to save, as the web client's download buttons
// do (src/client/telemetry.ts). The web player has no disk of its own.
mergeInto(LibraryManager.library, {
  LabDownload: function (namePtr, textPtr, typePtr) {
    var name = UTF8ToString(namePtr);
    var text = UTF8ToString(textPtr);
    var type = UTF8ToString(typePtr);
    var url = URL.createObjectURL(new Blob([text], { type: type }));
    var a = document.createElement('a');
    a.href = url;
    a.download = name;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(function () { URL.revokeObjectURL(url); }, 2000);
  },
});
