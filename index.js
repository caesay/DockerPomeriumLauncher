import { html, Component, render } from 'https://unpkg.com/htm/preact/standalone.module.js'

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

const ContainerItem = ({ iconUrl, id, name, navigateUrl, state, ipAddress, networkName, running }) => {

    if (_.isEmpty(navigateUrl)) {
        return html`
<div key=${id} class='container-item c-hidden'>
    <img class='container-img' src="${iconUrl}" />
    <div>
        <div class="container-title">${name}</div>
        <${RunningStatus} running=${running} />
    </div>
</div>`;
    } else {
        return html`
<a key=${id} href="${navigateUrl}" class='container-item c-launchable ${(running ? "c-on" : "c-off")}' target="_blank">
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

    render({ }, { containers = [], loading, error }) {

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
        var networks = _.orderBy(_.keys(grouped), c => c);

        return html`

<div class='network-group'>
    ${_.map(networks, (netName) => html`
        <span class="network-label">${netName}</span>
        <div class='container-grid'>
            ${_.map(_.orderBy(grouped[netName], i => i.name), (c) => html`<${ContainerItem} ...${c} />`)}
        </span>
    `)}
</div>
`;
    }
}


render(html`<${App} />`, document.body);