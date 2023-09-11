import { html, Component, render, useCallback, useRef, useState } from 'https://unpkg.com/htm/preact/standalone.module.js'

const useLongPress = (
    onLongPress,
    onClick,
    { shouldPreventDefault = true, delay = 300 } = {}
) => {
    const [longPressTriggered, setLongPressTriggered] = useState(false);
    const timeout = useRef();
    const target = useRef();

    const start = useCallback(
        event => {
            if (shouldPreventDefault && event.target) {
                event.target.addEventListener("touchend", preventDefault, {
                    passive: false
                });
                target.current = event.target;
            }
            timeout.current = setTimeout(() => {
                onLongPress(event);
                setLongPressTriggered(true);
            }, delay);
        },
        [onLongPress, delay, shouldPreventDefault]
    );

    const clear = useCallback(
        (event, shouldTriggerClick = true) => {
            timeout.current && clearTimeout(timeout.current);
            shouldTriggerClick && !longPressTriggered && onClick();
            setLongPressTriggered(false);
            if (shouldPreventDefault && target.current) {
                target.current.removeEventListener("touchend", preventDefault);
            }
        },
        [shouldPreventDefault, onClick, longPressTriggered]
    );

    return {
        onMouseDown: e => start(e),
        onTouchStart: e => start(e),
        onMouseUp: e => clear(e),
        onMouseLeave: e => clear(e, false),
        onTouchEnd: e => clear(e)
    };
};

const isTouchEvent = event => {
    return "touches" in event;
};

const preventDefault = event => {
    if (!isTouchEvent(event)) return;

    if (event.touches.length < 2 && event.preventDefault) {
        event.preventDefault();
    }
};

const LoadingWidget = () => html`
<svg class="pl" viewBox="0 0 128 128" width="128px" height="128px" xmlns="http://www.w3.org/2000/svg">
	<defs>
		<linearGradient id="pl-grad" x1="0" y1="0" x2="0" y2="1">
			<stop offset="0%" stop-color="hsl(193,90%,55%)" />
			<stop offset="100%" stop-color="hsl(223,90%,55%)" />
		</linearGradient>
	</defs>
	<circle class="pl__ring" r="56" cx="64" cy="64" fill="none" stroke="hsla(0,10%,10%,0.1)" stroke-width="16" stroke-linecap="round" />
	<path class="pl__worm" d="M92,15.492S78.194,4.967,66.743,16.887c-17.231,17.938-28.26,96.974-28.26,96.974L119.85,59.892l-99-31.588,57.528,89.832L97.8,19.349,13.636,88.51l89.012,16.015S81.908,38.332,66.1,22.337C50.114,6.156,36,15.492,36,15.492a56,56,0,1,0,56,0Z" fill="none" stroke="url(#pl-grad)" stroke-width="16" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="44 1111" stroke-dashoffset="10" />
</svg>
`;

const RunningStatus = ({ running }) => {
    if (running === true) {
        return html`
            <div class="running-status">
                <i class="fa-solid fa-play green"></i> ON
            </div>`;
    }
    if (running === false) {
        return html`
            <div class="running-status">
                <i class="fa-solid fa-stop red"></i> OFF
            </div>`;
    }
    return null;
}

const ContainerItem = ({ iconUrl, id, name, navigateUrl, state, ipAddress, networkName, running, onClick }) => {

    const performOnClick = useCallback((e) => {
        onClick(name, e);
    }, [name])

    const longPressEvent = useLongPress(performOnClick, () => { }, { delay: 500 });

    if (_.isEmpty(navigateUrl)) {
        return html`
<div key=${id} class='container-item c-hidden' ...${longPressEvent} onContextMenu=${performOnClick}>
    <img class='container-img' src="${iconUrl}" />
    <div>
        <div class="container-title">${name}</div>
        <${RunningStatus} running=${running} />
    </div>
</div>`;
    } else {
        return html`
<a key=${id} href="${navigateUrl}" class='container-item c-launchable ${(running ? "c-on" : "c-off")}' target="_blank" ...${longPressEvent} onContextMenu=${performOnClick}>
    <img class='container-img' src="${iconUrl}" />
    <div>
        <div class="container-title">${name}</div>
        <${RunningStatus} running=${running} />
    </div>
    <i class="fa-solid fa-rocket launch-icon"></i>
</div>`;
    }
};

class App extends Component {

    constructor() {
        super();
        this.state = {
            loading: true,
            containers: [],
            error: null,
            popupName: null,
        };
    }

    componentDidMount() {
        this.refresh();
    }

    refresh = async () => {
        try {
            const response = await fetch("/status");
            if (response.ok) {
                const containers = await response.json();
                this.setState({
                    loading: false,
                    containers,
                });
                setTimeout(this.refresh, 3000);
            } else {
                let msg = await response.text();
                console.log(msg);
                if (msg.length > 500)
                    msg = msg.substring(0, 500) + "....";

                this.setState({
                    error: msg,
                });

            }
        } catch (e) {
            this.setState({
                error: e.toString(),
            });
            console.log(e);
        }
    }

    handleItemClick = (name, e) => {
        e.preventDefault();
        this.setState({ popupName: name });
    }

    handleItemClose = () => {
        this.setState({ popupName: null });
    }

    render({ }, { containers = [], loading, error, popupName }) {

        if (error) {
            return html`<div class='error-box'>${error}</div>`;
        }

        if (loading) {
            return html`<${LoadingWidget} />`;
        }

        if (_.isEmpty(containers)) {
            return html`<div class='error-box'>There were no containers returned by the API.</div>`;
        }

        const grouped = _.groupBy(containers, c => c.networkName);
        const networks = _.orderBy(_.keys(grouped), c => c);

        let popupItem;
        if (popupName) {
            popupItem = _.find(this.state.containers, (c) => c.name == popupName);
            console.log(popupItem);
        }

        return html`
<div class='network-group'>
    ${_.map(networks, (netName) => html`
        <span class="network-label">${netName}</span>
        <div class='container-grid'>
            ${_.map(_.orderBy(grouped[netName], i => i.name), (c) => html`<${ContainerItem} onClick=${this.handleItemClick} ...${c} />`)}
        </span>
    `)}
</div>
${popupName && html`
<div class="popup-overlay" onClick=${this.handleItemClose} />
<div class="popup-options">
    <div class="popup-title">
        <span>${popupName}</span><i class="fa-solid fa-close close-icon" onClick=${this.handleItemClose}></i>
    </div>
    <span>${popupItem.networkName} - ${popupItem.ipAddress}</span> <br/>
    <${RunningStatus} running=${popupItem.running} />
    ${_.map(popupItem.ports, p => html`<span>${p.privatePort}::${p.publicPort}, </span>`)} 
    
    ${popupItem.running === true && html`<a class="popup-item" href="/stop/${popupName}"><i class="fa-solid fa-stop fa-fixed-width"></i> Stop Container</a>
                                         <a class="popup-item" href="/restart/${popupName}"><i class="fa-solid fa-rotate-right fa-fixed-width"></i> Restart Container</a>`}
    ${popupItem.running === false && html`<a class="popup-item" href="/start/${popupName}"><i class="fa-solid fa-play fa-fixed-width"></i> Start Container</a>`}
    ${popupItem.navigateUrl && html`<a class="popup-item" href="${popupItem.navigateUrl}"><i class="fa-solid fa-link fa-fixed-width"></i> Navigate To</a>`}
    ${_.map(popupItem.extraActions, (e, k) => (html`<a class="popup-item" target="_blank" href="${e}"><i class="fa-solid fa-fixed-width">${k.split(' ')[0]}</i> ${_.join(_.tail(k.split(' ')), " ")}</a>`))}
</div>
`}
`;
    }
}


render(html`<${App} />`, document.body);