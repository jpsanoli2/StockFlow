window.stockFlow = {
    download(name, content) {
        const url = URL.createObjectURL(new Blob(['\ufeff', content], {type: 'text/csv;charset=utf-8'}));
        const a = document.createElement('a'); a.href = url; a.download = name;
        document.body.appendChild(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
};
