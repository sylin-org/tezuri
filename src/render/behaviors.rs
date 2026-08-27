//!  Behavior scripts Tezuri injects into emitted pages.
pub(crate) const BEHAVIOR_JS: &str = r##"<script>
(function () {
  var lb = null;
  function open(src) {
    if (!lb) {
      lb = document.createElement('div');
      lb.className = 'lightbox';
      lb.innerHTML = '<img alt="">';
      document.body.appendChild(lb);
      lb.addEventListener('click', function () { this.classList.remove('on'); });
      document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') lb.classList.remove('on');
      });
    }
    lb.querySelector('img').src = src;
    lb.classList.add('on');
  }
  document.querySelectorAll('.gallery img, .article-prose > p > img')
    .forEach(function (img) { img.addEventListener('click', function () { open(img.src); }); });

  // Scroll-spy: the toc's current section follows the reader.
  var links = [].slice.call(document.querySelectorAll('.toc a[href^="#"]'));
  if (!links.length || !('IntersectionObserver' in window)) return;
  var byId = {};
  links.forEach(function (l) { byId[l.getAttribute('href').slice(1)] = l; });
  var obs = new IntersectionObserver(function (entries) {
    entries.forEach(function (en) {
      if (!en.isIntersecting) return;
      links.forEach(function (l) { l.classList.remove('current'); });
      var link = byId[en.target.id];
      if (link) link.classList.add('current');
    });
  }, { rootMargin: '-80px 0px -70% 0px', threshold: 0 });
  Object.keys(byId).forEach(function (id) {
    var el = document.getElementById(id);
    if (el) obs.observe(el);
  });
})();
</script>"##;
