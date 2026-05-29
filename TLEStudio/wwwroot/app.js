window.tleStudioScroll = {
  _y: 0,
  capture: function () {
    this._y = window.scrollY || window.pageYOffset || 0;
  },
  restore: function () {
    window.scrollTo({ top: this._y, behavior: "auto" });
  }
};
