'use strict';

function tr(source, ...values) {
 const dictionary = window.frameNoteEnglish;
 const template = dictionary && Object.hasOwn(dictionary, source) ? dictionary[source] : source;
 return values.length ? template.replace(/\{(\d+)\}/g, (_, index) => String(values[index])) : template;
}

// Translate only bundled markup, before media, device names or annotations are loaded.
if (window.frameNoteEnglish) {
 document.documentElement.lang = 'en';
 function localizeMarkup(root) {
  const nodes = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  while (nodes.nextNode()) {
   const node = nodes.currentNode;
   const key = node.data.trim();
   if (Object.hasOwn(window.frameNoteEnglish, key)) node.data = node.data.replace(key, () => tr(key));
  }
  for (const element of root.querySelectorAll('*')) {
   for (const name of ['aria-label', 'aria-description', 'title', 'placeholder', 'alt']) {
    if (element.hasAttribute(name)) element.setAttribute(name, tr(element.getAttribute(name)));
   }
   if (element.tagName === 'TEMPLATE') localizeMarkup(element.content);
  }
 }
 localizeMarkup(document);
}
