#target photoshop
app.bringToFront();

function runAction(actionName, setName) {
  try {
    app.activeDocument.suspendHistory("Run " + setName + " › " + actionName, function () {
      app.doAction(actionName, setName);
    });
  } catch (e) {
    alert(
      "Couldn’t run Action.\n\n" +
      "Action: " + actionName + "\n" +
      "Set: " + setName + "\n\n" +
      "Tips:\n" +
      "• In the Actions panel, the folder name is the Action Set.\n" +
      "• The item under it is the Action.\n" +
      "• Make sure the set is loaded and names match exactly (case-sensitive)."
    );
  }
}

app.doAction("BSXT_ACTION (Click Here)", "BSXT");
