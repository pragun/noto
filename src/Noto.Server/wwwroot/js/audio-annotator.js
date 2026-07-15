window.notoAudio = window.notoAudio || {};

let wavesurfer = null;
let audioDotnetRef = null;

notoAudio.init = async function (ref, container, audioUrl) {
    audioDotnetRef = ref;

    // Dynamically load wavesurfer if not present
    if (!window.WaveSurfer) {
        await new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = '/lib/js/wavesurfer.min.js';
            script.onload = resolve;
            script.onerror = reject;
            document.head.appendChild(script);
        });
    }

    if (wavesurfer) {
        wavesurfer.destroy();
    }

    wavesurfer = WaveSurfer.create({
        container: container,
        waveColor: '#dad4c8',
        progressColor: '#7a9e7e',
        cursorColor: '#4a6b4e',
        cursorWidth: 2,
        barWidth: 2,
        barGap: 1,
        barRadius: 1,
        height: 80,
        normalize: true,
        backend: 'WebAudio',
    });

    wavesurfer.on('timeupdate', function (time) {
        audioDotnetRef.invokeMethodAsync('OnTimeUpdate', time);
    });

    wavesurfer.on('click', function (relativeX) {
        const time = relativeX * wavesurfer.getDuration();
        audioDotnetRef.invokeMethodAsync('OnTimeUpdate', time);
    });

    wavesurfer.on('ready', function () {
        audioDotnetRef.invokeMethodAsync('OnReady', wavesurfer.getDuration());
    });

    if (audioUrl) {
        wavesurfer.load(audioUrl);
    }
};

notoAudio.loadUrl = function (url) {
    if (wavesurfer) wavesurfer.load(url);
};

notoAudio.loadBlob = function (blob) {
    if (wavesurfer) wavesurfer.loadBlob(blob);
};

notoAudio.play = function () {
    if (wavesurfer) wavesurfer.playPause();
};

notoAudio.pause = function () {
    if (wavesurfer) wavesurfer.pause();
};

notoAudio.seekTo = function (seconds) {
    if (wavesurfer && wavesurfer.getDuration() > 0) {
        wavesurfer.setTime(seconds);
    }
};

notoAudio.getCurrentTime = function () {
    return wavesurfer ? wavesurfer.getCurrentTime() : 0;
};

notoAudio.getDuration = function () {
    return wavesurfer ? wavesurfer.getDuration() : 0;
};

notoAudio.destroy = function () {
    if (wavesurfer) {
        wavesurfer.destroy();
        wavesurfer = null;
    }
};
